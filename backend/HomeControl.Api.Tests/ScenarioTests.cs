using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using HomeControl.Api.Blink;
using HomeControl.Api.Data;
using HomeControl.Api.Models;
using HomeControl.Api.Scenarios;
using HomeControl.Api.Tests.Fakes;
using HomeControl.Api.Tuya;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HomeControl.Api.Tests;

// Regel "Tür geht auf → Blink scharf". Tür und Blink sind gespielt, nichts wird wirklich scharfgeschaltet.
public class ScenarioTests
{
    private const string BridgeKey = "test-bridge-schluessel-nur-fuer-tests-012345";
    private const string DoorGoogleId = "google-tuer";

    private sealed class Factory(double connectedSeconds = 6) : HomeControlApiFactory
    {
        public FakeBlinkCameraClient Blink { get; } = new();
        public FakeTuyaHandler Tuya { get; } = new();

        protected override Dictionary<string, string?> Settings => new()
        {
            ["SensorBridge:Key"] = BridgeKey,
            ["Tuya:ClientId"] = "test-client",
            ["Tuya:ClientSecret"] = "test-secret",
            ["GoogleHome:ConnectedSeconds"] = connectedSeconds.ToString(CultureInfo.InvariantCulture),
        };

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.ConfigureTestServices(services =>
            {
                services.AddSingleton<IBlinkCameraClient>(Blink);
                services.AddHttpClient<TuyaClient>().ConfigurePrimaryHttpMessageHandler(() => Tuya);
                services.AddSingleton<IStartupFilter, FakeRemoteIpStartupFilter>();
            });
        }
    }

    private static object Door(bool open, bool online = true) => new { id = DoorGoogleId, name = "Tür", online, on = (bool?)null, open };

    private static async Task PollAsync(HttpClient bridge, object device)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/google-home/bridge/poll")
        {
            Content = JsonContent.Create(new { devices = new[] { device } }),
        };
        request.Headers.Add("X-Sensor-Key", BridgeKey);
        (await bridge.SendAsync(request)).EnsureSuccessStatusCode();
    }

    private static async Task<(HttpClient Admin, HttpClient Bridge, Guid DoorId)> PrepareAsync(Factory factory, string? googleId = DoorGoogleId)
    {
        var tokens = await factory.GetTokensAsync();
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var room = new Room { Name = "Flur" };
        var door = new Device { Name = "Tür", Type = "DoorSensor", RoomId = room.Id, TuyaDeviceId = "tuyatuer", GoogleHomeDeviceId = googleId, LastValue = "ZU" };
        db.Rooms.Add(room);
        db.Devices.Add(door);
        await db.SaveChangesAsync();
        return (factory.CreateClientWithToken(tokens.AdminToken), factory.CreateClient(), door.Id);
    }

    private static async Task<ScenarioEndpoints.ScenarioResponse> CreateAsync(HttpClient admin, Guid doorId, bool enable = true)
    {
        var response = await admin.PostAsJsonAsync("/api/scenarios", new { name = "Tür scharf", triggerDeviceId = doorId, blinkSystemId = "77" });
        response.EnsureSuccessStatusCode();
        var scenario = (await response.Content.ReadFromJsonAsync<ScenarioEndpoints.ScenarioResponse>())!;
        if (enable)
            (await admin.PutAsJsonAsync($"/api/scenarios/{scenario.Id}/enabled", new { enabled = true })).EnsureSuccessStatusCode();
        return scenario;
    }

    private static async Task<List<ScenarioRun>> RunsAsync(Factory factory)
    {
        using var scope = factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().ScenarioRuns.AsNoTracking().ToListAsync();
    }

    private static async Task<ScenarioRun> WaitForFinishedRunAsync(Factory factory)
    {
        for (var attempt = 0; attempt < 150; attempt++)
        {
            var run = (await RunsAsync(factory)).FirstOrDefault(r => r.FinishedAtUtc is not null);
            if (run is not null) return run;
            await Task.Delay(20);
        }
        throw new TimeoutException("Die Regel lief nicht.");
    }

    // Kurz warten, damit eine fälschlich ausgelöste Regel auffallen würde.
    private static Task SettleAsync() => Task.Delay(300);

    [Fact]
    public async Task Neue_Regel_startet_ausgeschaltet()
    {
        using var factory = new Factory();
        var (admin, _, doorId) = await PrepareAsync(factory);
        Assert.False((await CreateAsync(admin, doorId, enable: false)).Enabled);
    }

    [Fact]
    public async Task Ausgeschaltete_Regel_loest_nicht_aus()
    {
        using var factory = new Factory();
        var (admin, bridge, doorId) = await PrepareAsync(factory);
        await CreateAsync(admin, doorId, enable: false);

        await PollAsync(bridge, Door(open: false));
        await PollAsync(bridge, Door(open: true));
        await SettleAsync();

        Assert.Empty(factory.Blink.ArmRequests);
    }

    [Fact]
    public async Task Frischer_Wechsel_schaltet_scharf_und_zeigt_die_Bestaetigung()
    {
        using var factory = new Factory();
        var (admin, bridge, doorId) = await PrepareAsync(factory);
        await CreateAsync(admin, doorId);

        await PollAsync(bridge, Door(open: false));
        await PollAsync(bridge, Door(open: true));

        var run = await WaitForFinishedRunAsync(factory);
        Assert.Equal(ScenarioOutcomes.Confirmed, run.Outcome);
        Assert.Equal("tuer", run.Trigger);
        Assert.Equal(["77"], factory.Blink.ArmRequests);
    }

    [Fact]
    public async Task Fehlende_Bestaetigung_wird_im_Verlauf_gezeigt()
    {
        using var factory = new Factory();
        factory.Blink.Arm = _ => new BlinkArmResult(true, false, false);
        var (admin, bridge, doorId) = await PrepareAsync(factory);
        await CreateAsync(admin, doorId);

        await PollAsync(bridge, Door(open: false));
        await PollAsync(bridge, Door(open: true));

        Assert.Equal(ScenarioOutcomes.NotConfirmed, (await WaitForFinishedRunAsync(factory)).Outcome);
    }

    [Fact]
    public async Task Bereits_offene_Tuer_beim_Start_loest_nicht_aus()
    {
        using var factory = new Factory();
        var (admin, bridge, doorId) = await PrepareAsync(factory);
        await CreateAsync(admin, doorId);

        await PollAsync(bridge, Door(open: true));
        await PollAsync(bridge, Door(open: true));
        await SettleAsync();

        Assert.Empty(factory.Blink.ArmRequests);
    }

    [Fact]
    public async Task Offene_Tuer_nach_einer_Trennung_ist_kein_frisches_Signal()
    {
        using var factory = new Factory(connectedSeconds: 0.1);
        var (admin, bridge, doorId) = await PrepareAsync(factory);
        await CreateAsync(admin, doorId);

        await PollAsync(bridge, Door(open: false));
        await Task.Delay(250);
        await PollAsync(bridge, Door(open: true));
        await SettleAsync();

        Assert.Empty(factory.Blink.ArmRequests);
    }

    [Fact]
    public async Task Doppelte_Meldungen_loesen_nur_einmal_aus()
    {
        using var factory = new Factory();
        var (admin, bridge, doorId) = await PrepareAsync(factory);
        await CreateAsync(admin, doorId);

        await PollAsync(bridge, Door(open: false));
        await PollAsync(bridge, Door(open: true));
        await PollAsync(bridge, Door(open: true));
        await PollAsync(bridge, Door(open: true));
        await WaitForFinishedRunAsync(factory);
        await SettleAsync();

        Assert.Single(factory.Blink.ArmRequests);
    }

    [Fact]
    public async Task Flatternde_Tuer_loest_innerhalb_der_Sperrzeit_nur_einmal_aus()
    {
        using var factory = new Factory();
        var (admin, bridge, doorId) = await PrepareAsync(factory);
        await CreateAsync(admin, doorId);

        await PollAsync(bridge, Door(open: false));
        await PollAsync(bridge, Door(open: true));
        await WaitForFinishedRunAsync(factory);
        await PollAsync(bridge, Door(open: false));
        await PollAsync(bridge, Door(open: true));
        await SettleAsync();

        Assert.Single(factory.Blink.ArmRequests);
    }

    [Fact]
    public async Task Offline_Tuer_loest_nicht_aus()
    {
        using var factory = new Factory();
        var (admin, bridge, doorId) = await PrepareAsync(factory);
        await CreateAsync(admin, doorId);

        await PollAsync(bridge, Door(open: false));
        await PollAsync(bridge, Door(open: true, online: false));
        await SettleAsync();

        Assert.Empty(factory.Blink.ArmRequests);
    }

    [Fact]
    public async Task Tuya_ueberschreibt_das_Tuersignal_nicht_liefert_aber_weiter_den_Akku()
    {
        using var factory = new Factory();
        factory.Tuya.DeviceListResponse = _ => TuyaJson.DeviceList(false, null, ("tuyatuer", "Tür", true));
        factory.Tuya.StatusResponse = (_, _) =>
            """{ "success": true, "result": [ { "code": "doorcontact_state", "value": true }, { "code": "battery_percentage", "value": 23 } ] }""";
        var (admin, bridge, doorId) = await PrepareAsync(factory);
        await PollAsync(bridge, Door(open: false));

        (await admin.PostAsync("/api/tuya/sync", null)).EnsureSuccessStatusCode();

        using var scope = factory.Services.CreateScope();
        var door = await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().Devices.AsNoTracking().FirstAsync(d => d.Id == doorId);
        Assert.Equal("ZU", door.LastValue);
        Assert.Equal(23, door.BatteryPercent);
    }

    [Fact]
    public async Task Regel_verlangt_ein_frisches_Tuersignal()
    {
        using var factory = new Factory();
        var (admin, _, doorId) = await PrepareAsync(factory, googleId: null);
        var response = await admin.PostAsJsonAsync("/api/scenarios", new { name = "x", triggerDeviceId = doorId, blinkSystemId = "77" });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Unbekanntes_Blink_System_wird_abgelehnt()
    {
        using var factory = new Factory();
        var (admin, _, doorId) = await PrepareAsync(factory);
        var response = await admin.PostAsJsonAsync("/api/scenarios", new { name = "x", triggerDeviceId = doorId, blinkSystemId = "999" });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Normale_Benutzer_duerfen_keine_Regeln_sehen()
    {
        using var factory = new Factory();
        var tokens = await factory.GetTokensAsync();
        using var user = factory.CreateClientWithToken(tokens.UserToken);
        Assert.Equal(HttpStatusCode.Forbidden, (await user.GetAsync("/api/scenarios")).StatusCode);
    }

    [Fact]
    public async Task Tuersensor_laesst_sich_mit_gemeldetem_Kontakt_verknuepfen()
    {
        using var factory = new Factory();
        var (admin, bridge, doorId) = await PrepareAsync(factory, googleId: null);
        await PollAsync(bridge, Door(open: true));

        using var scope = factory.Services.CreateScope();
        var door = await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().Devices.AsNoTracking().FirstAsync(d => d.Id == doorId);
        var response = await admin.PutAsJsonAsync($"/api/devices/{doorId}", new
        {
            name = door.Name, type = door.Type, roomId = door.RoomId, tuyaDeviceId = door.TuyaDeviceId, googleHomeDeviceId = DoorGoogleId,
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}

// Das Rahmenformat des Blink-Livebilds, ohne Netzwerk und ohne Kamera.
public class BlinkLiveProbeTests
{
    [Fact]
    public void Anmelderahmen_hat_das_erwartete_Format()
    {
        var frame = BlinkLiveProbe.BuildAuthFrame("SERIAL123", 7, "abc");

        Assert.Equal(122, frame.Length);
        Assert.Equal(new byte[] { 0, 0, 0, 0x28 }, frame[..4]);
        Assert.Equal("SERIAL123", Encoding.UTF8.GetString(frame[8..17]));
        Assert.Equal(7, frame[27]);
        Assert.Equal(new byte[] { 0, 0, 0, 1 }, frame[^4..]);
    }

    [Fact]
    public void Blink_Adresse_wird_gelesen()
    {
        var server = BlinkLiveProbe.ParseServer("immis://live.prod.immedia-semi.com:443/abc__def?client_id=5");
        Assert.NotNull(server);
        Assert.Equal(443, server.Port);
        Assert.Equal(5, server.ClientId);
        Assert.Equal("abc", server.ConnectionId);
    }

    [Theory]
    [InlineData("immis://boese.example.com:443/abc__def")]
    [InlineData("rtsps://live.prod.immedia-semi.com:443/abc__def")]
    [InlineData("kein adresse")]
    [InlineData(null)]
    public void Fremde_Adressen_werden_abgelehnt(string? server) => Assert.Null(BlinkLiveProbe.ParseServer(server));

    [Fact]
    public void Nur_MPEG_TS_zaehlt_als_Video()
    {
        Assert.True(BlinkLiveProbe.IsVideoPayload(0, [0x47, 1]));
        Assert.False(BlinkLiveProbe.IsVideoPayload(0, [0x48, 1]));
        Assert.False(BlinkLiveProbe.IsVideoPayload(0x0A, [0x47, 1]));
        Assert.False(BlinkLiveProbe.IsVideoPayload(0, []));
    }
}
