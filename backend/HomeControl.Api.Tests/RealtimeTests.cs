using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Net.ServerSentEvents;
using System.Text.Json;
using HomeControl.Api.Contracts;
using HomeControl.Api.Data;
using HomeControl.Api.Demo;
using HomeControl.Api.Endpoints;
using HomeControl.Api.Models;
using HomeControl.Api.Realtime;
using HomeControl.Api.Tests.Fakes;
using HomeControl.Api.Tuya;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Testing = Microsoft.Extensions.Time.Testing;

namespace HomeControl.Api.Tests;

// Echtzeit-Kanal zum Browser (docs/ECHTZEIT.md): Verteilstelle, Strom-Logik und Endpunkt.
// Tuya wird hier nie echt gefragt: FakeTuyaHandler bzw. im Demo-Modus DemoTuyaHandler spielen die Cloud.
public class RealtimeTests
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private static DeviceChangeEvent Change(params Guid[] ids) =>
        new(ids, DeviceChangeSources.TuyaSync, DateTimeOffset.UnixEpoch);

    // ---------- Verteilstelle ----------

    [Fact]
    public void Jede_Verbindung_bekommt_jede_Aenderung_und_Dispose_meldet_ab()
    {
        var hub = new DeviceChangeHub();
        var a = hub.TrySubscribe()!;
        var b = hub.TrySubscribe()!;

        hub.Publish(Change(Guid.NewGuid()));

        Assert.True(a.Reader.TryRead(out _));
        Assert.True(b.Reader.TryRead(out _));
        a.Dispose();
        Assert.Equal(1, hub.SubscriberCount);
        hub.Publish(Change(Guid.NewGuid()));
        Assert.False(a.Reader.TryRead(out _));
        Assert.True(b.Reader.TryRead(out _));
    }

    [Fact]
    public void Volle_Warteschlange_verliert_die_aelteste_Meldung_und_blockiert_nicht()
    {
        var hub = new DeviceChangeHub();
        var slow = hub.TrySubscribe()!;
        var last = Guid.NewGuid();

        for (var i = 0; i < 1000; i++)
        {
            hub.Publish(Change(Guid.NewGuid()));
        }

        hub.Publish(Change(last));

        DeviceChangeEvent? newest = null;
        var count = 0;
        while (slow.Reader.TryRead(out var change))
        {
            newest = change;
            count++;
        }

        Assert.Equal(32, count);
        Assert.Equal(last, newest!.DeviceIds.Single());
    }

    [Fact]
    public void Zu_viele_Verbindungen_werden_abgelehnt()
    {
        var hub = new DeviceChangeHub();
        for (var i = 0; i < DeviceChangeHub.MaxSubscribers; i++)
        {
            Assert.NotNull(hub.TrySubscribe());
        }

        Assert.Null(hub.TrySubscribe());
    }

    // ---------- Strom: Reihenfolge, Lebenszeichen, Ende ----------

    [Fact]
    public async Task Strom_meldet_erst_connected_dann_Aenderungen_dann_Lebenszeichen_und_endet_beim_Tokenablauf()
    {
        var clock = new Testing.FakeTimeProvider(DateTimeOffset.UnixEpoch);
        var hub = new DeviceChangeHub();
        var closeAt = clock.GetUtcNow() + TimeSpan.FromSeconds(60);
        await using var stream = RealtimeEndpoints
            .ReadEventsAsync(hub.TrySubscribe()!, closeAt, clock, CancellationToken.None)
            .GetAsyncEnumerator();

        Assert.True(await stream.MoveNextAsync());
        Assert.Equal(RealtimeEndpoints.ConnectedEvent, stream.Current.EventType);

        var id = Guid.NewGuid();
        hub.Publish(Change(id));
        Assert.True(await stream.MoveNextAsync());
        Assert.Equal(RealtimeEndpoints.ChangeEvent, stream.Current.EventType);
        Assert.Equal(id, stream.Current.Data!.DeviceIds.Single());

        // Nichts passiert: Nach 25 s kommt ein Lebenszeichen.
        var ping = stream.MoveNextAsync();
        await WaitUntilPendingAsync(ping);
        clock.Advance(RealtimeEndpoints.Heartbeat);
        Assert.True(await ping);
        Assert.Equal(RealtimeEndpoints.PingEvent, stream.Current.EventType);

        // Bis zum Ablauf des Tokens: noch ein Lebenszeichen (nach 50 s), dann ist Schluss, und die Verbindung ist abgemeldet.
        var ping2 = stream.MoveNextAsync();
        await WaitUntilPendingAsync(ping2);
        clock.Advance(RealtimeEndpoints.Heartbeat);
        Assert.True(await ping2);
        var end = stream.MoveNextAsync();
        await WaitUntilPendingAsync(end);
        clock.Advance(TimeSpan.FromSeconds(10));
        Assert.True(await end);   // letztes Lebenszeichen genau am Ablauf
        Assert.False(await stream.MoveNextAsync());
        Assert.Equal(0, hub.SubscriberCount);
    }

    [Fact]
    public async Task Strom_endet_sauber_wenn_der_Browser_die_Verbindung_trennt()
    {
        var hub = new DeviceChangeHub();
        using var disconnect = new CancellationTokenSource();
        await using var stream = RealtimeEndpoints
            .ReadEventsAsync(hub.TrySubscribe()!, DateTimeOffset.MaxValue, TimeProvider.System, disconnect.Token)
            .GetAsyncEnumerator(disconnect.Token);
        await stream.MoveNextAsync();

        var next = stream.MoveNextAsync();
        await disconnect.CancelAsync();

        Assert.False(await next);
        Assert.Equal(0, hub.SubscriberCount);
    }

    // FakeTimeProvider wirkt nur auf Timer, die schon laufen: Dem Strom kurz Zeit geben, seinen Timer zu starten.
    private static async Task WaitUntilPendingAsync(ValueTask<bool> pending)
    {
        Assert.False(pending.IsCompleted);
        await Task.Delay(50);
    }

    // ---------- Abgleich meldet Änderungen ----------

    private sealed class FakeTuyaApiFactory : HomeControlApiFactory
    {
        public FakeTuyaHandler Tuya { get; } = new();

        protected override Dictionary<string, string?> Settings => new()
        {
            ["Tuya:ClientId"] = "test-client",
            ["Tuya:ClientSecret"] = "test-secret",
        };

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.ConfigureTestServices(services =>
                services.AddHttpClient<TuyaClient>().ConfigurePrimaryHttpMessageHandler(() => Tuya));
        }

        public async Task<Guid> AddLinkedDeviceAsync()
        {
            using var scope = Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var device = new Device { Name = "Lampe", Type = "Light", Room = new Room { Name = "Flur" }, TuyaDeviceId = "abc123" };
            db.Devices.Add(device);
            await db.SaveChangesAsync();
            return device.Id;
        }
    }

    [Fact]
    public async Task Abgleich_meldet_nur_wirkliche_Aenderungen_und_erst_nach_dem_Speichern()
    {
        using var factory = new FakeTuyaApiFactory();
        var deviceId = await factory.AddLinkedDeviceAsync();
        factory.Tuya.DeviceListResponse = _ => TuyaJson.DeviceList(false, null, ("abc123", "Lampe", true));
        var hub = factory.Services.GetRequiredService<DeviceChangeHub>();
        using var subscription = hub.TrySubscribe()!;

        async Task SyncAsync()
        {
            using var scope = factory.Services.CreateScope();
            await scope.ServiceProvider.GetRequiredService<TuyaDeviceSyncer>().ReadAllAsync(apply: true, CancellationToken.None);
        }

        // 1. Lauf: Zustand und Online-Status sind neu.
        await SyncAsync();
        Assert.True(subscription.Reader.TryRead(out var first));
        Assert.Equal(deviceId, first!.DeviceIds.Single());
        Assert.Equal(DeviceChangeSources.TuyaSync, first.Source);

        // 2. Lauf ohne Änderung: keine Meldung, der Browser lädt nicht unnötig neu.
        await SyncAsync();
        Assert.False(subscription.Reader.TryRead(out _));

        // 3. Lauf: Schalter an. Beim Lesen der Meldung steht der neue Wert schon in der Datenbank.
        factory.Tuya.StatusResponse = (_, _) => TuyaJson.Status(true);
        await SyncAsync();
        Assert.True(subscription.Reader.TryRead(out _));
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.Equal(TuyaSwitchRecorder.OnText, (await db.Devices.SingleAsync()).LastValue);

        // 4. Lauf: Gerät geht offline, Schalter gleich: Die LED muss sich trotzdem ändern.
        factory.Tuya.DeviceListResponse = _ => TuyaJson.DeviceList(false, null, ("abc123", "Lampe", false));
        await SyncAsync();
        Assert.True(subscription.Reader.TryRead(out var offline));
        Assert.Equal(deviceId, offline!.DeviceIds.Single());
    }

    [Fact]
    public async Task Nur_lesen_ohne_Speichern_meldet_nichts()
    {
        using var factory = new FakeTuyaApiFactory();
        await factory.AddLinkedDeviceAsync();
        using var subscription = factory.Services.GetRequiredService<DeviceChangeHub>().TrySubscribe()!;
        using var scope = factory.Services.CreateScope();

        await scope.ServiceProvider.GetRequiredService<TuyaDeviceSyncer>().ReadAllAsync(apply: false, CancellationToken.None);

        Assert.False(subscription.Reader.TryRead(out _));
    }

    // ---------- Endpunkt ----------

    [Fact]
    public async Task Endpunkt_verlangt_ein_JWT_und_lehnt_den_Agenten_Schluessel_ab()
    {
        using var factory = new LoginRequiredApiFactory();
        using var anonymous = factory.CreateClient();
        using var agent = factory.CreateClientWithApiKey(LoginRequiredApiFactory.AgentApiKey);
        using var invalid = factory.CreateClientWithToken("kein.gueltiges.token");

        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/live/devices")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await agent.GetAsync("/api/live/devices")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await invalid.GetAsync("/api/live/devices")).StatusCode);
    }

    // Der ganze Weg im Demo-Modus: simulierte Tür → Fake-Cloud → echter TuyaClient und Syncer → Datenbank → SSE-Antwort.
    [Fact]
    public async Task Simulierte_Tuer_kommt_als_Ereignis_im_Strom_an_und_REST_liefert_den_neuen_Wert()
    {
        using var factory = new DemoModeTests.DemoApiFactory();
        using var client = await factory.CreateDemoClientAsync();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/live/devices");
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));

        // ResponseHeadersRead: nicht auf das Ende warten, der Strom endet ja nicht von selbst.
        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
        Assert.Equal("text/event-stream", response.Content.Headers.ContentType?.MediaType);
        await using var body = await response.Content.ReadAsStreamAsync(timeout.Token);
        await using var events = SseParser.Create(body).EnumerateAsync(timeout.Token).GetAsyncEnumerator(timeout.Token);

        Assert.True(await events.MoveNextAsync());
        Assert.Equal(RealtimeEndpoints.ConnectedEvent, events.Current.EventType);

        var door = (await client.GetFromJsonAsync<List<DeviceResponse>>("/api/devices"))!.Single(d => d.Name == "Haustür");
        var before = door.LastValue;
        var toggle = await client.PostAsync("/api/demo/simulation/tuer", null, timeout.Token);
        Assert.Equal(HttpStatusCode.NoContent, toggle.StatusCode);

        Assert.True(await events.MoveNextAsync());
        Assert.Equal(RealtimeEndpoints.ChangeEvent, events.Current.EventType);
        var change = JsonSerializer.Deserialize<DeviceChangeEvent>(events.Current.Data, Json)!;
        Assert.Contains(door.Id, change.DeviceIds);

        var after = (await client.GetFromJsonAsync<List<DeviceResponse>>("/api/devices"))!.Single(d => d.Id == door.Id);
        Assert.NotEqual(before, after.LastValue);
    }

    [Fact]
    public async Task Simulation_gibt_es_nur_im_Demo_und_nur_fuer_Admins()
    {
        using var normal = new HomeControlApiFactory();
        var (adminToken, _) = await normal.GetTokensAsync();
        using var admin = normal.CreateClientWithToken(adminToken);
        Assert.Equal(HttpStatusCode.NotFound, (await admin.PostAsync("/api/demo/simulation/tuer", null)).StatusCode);

        using var demo = new DemoModeTests.DemoApiFactory();
        using var anonymous = demo.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.PostAsync("/api/demo/simulation/tuer", null)).StatusCode);
    }
}
