using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using HomeControl.Api.Contracts;
using HomeControl.Api.Data;
using HomeControl.Api.Models;
using HomeControl.Api.Status;
using HomeControl.Api.Tests.Fakes;
using HomeControl.Api.Tuya;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace HomeControl.Api.Tests;

// Der Rückweg zu Muju Home. Die App spielt der Test selbst, kein echtes Gerät wird geschaltet.
public class GoogleHomeEndpointTests
{
    private const string BridgeKey = "test-bridge-schluessel-nur-fuer-tests-012345";
    private const string GoogleId = "google-steckdose";
    private const string TuyaId = "alterTuyaEintrag";

    private sealed class Factory(double pickupSeconds = 4, double confirmSeconds = 12) : HomeControlApiFactory
    {
        public FakeTuyaHandler Tuya { get; } = new();

        protected override Dictionary<string, string?> Settings => new()
        {
            ["SensorBridge:Key"] = BridgeKey,
            ["Tuya:ClientId"] = "test-client",
            ["Tuya:ClientSecret"] = "test-secret",
            ["GoogleHome:PickupSeconds"] = pickupSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["GoogleHome:ConfirmSeconds"] = confirmSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture),
        };

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.ConfigureTestServices(services =>
            {
                services.AddHttpClient<TuyaClient>().ConfigurePrimaryHttpMessageHandler(() => Tuya);
                // Der TestServer hat keine Client-Adresse. Die Bridge verlangt aber Loopback.
                services.AddSingleton<IStartupFilter, FakeRemoteIpStartupFilter>();
            });
        }
    }

    private record Command(Guid Id, string DeviceId, bool On);
    private record PollAnswer(List<Command> Commands);

    private static object Socket(bool? on, bool online = true, string name = "Stehlampe", string id = GoogleId) =>
        new { id, name, online, on };

    private static HttpRequestMessage BridgeRequest(string path, object body, string? key = BridgeKey, string? remoteIp = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, path) { Content = JsonContent.Create(body) };
        if (key is not null) request.Headers.Add("X-Sensor-Key", key);
        if (remoteIp is not null) request.Headers.Add(FakeRemoteIpStartupFilter.HeaderName, remoteIp);
        return request;
    }

    private static async Task<List<Command>> PollAsync(HttpClient bridge, params object[] devices)
    {
        var response = await bridge.SendAsync(BridgeRequest("/api/google-home/bridge/poll", new { devices }));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<PollAnswer>())!.Commands;
    }

    private static Task<HttpResponseMessage> ReportAsync(HttpClient bridge, Guid commandId, bool? on, string? error = null) =>
        bridge.SendAsync(BridgeRequest($"/api/google-home/bridge/commands/{commandId}/result", new { on, error }));

    // Fragt wie die App nach, bis der Befehl da ist.
    private static async Task<Command> WaitForCommandAsync(HttpClient bridge, params object[] devices)
    {
        for (var attempt = 0; attempt < 100; attempt++)
        {
            var commands = await PollAsync(bridge, devices);
            if (commands.Count > 0) return Assert.Single(commands);
            await Task.Delay(20);
        }
        throw new TimeoutException("Es kam kein Befehl an.");
    }

    private static async Task<(HttpClient Admin, HttpClient Bridge, Guid DeviceId)> PrepareAsync(
        Factory factory, string name = "Stehlampe", string? googleId = GoogleId)
    {
        var tokens = await factory.GetTokensAsync();
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var room = new Room { Name = "Wohnzimmer" };
        var device = new Device { Name = name, Type = "Light", RoomId = room.Id, TuyaDeviceId = TuyaId, GoogleHomeDeviceId = googleId, LastValue = "AUS" };
        db.Rooms.Add(room);
        db.Devices.Add(device);
        await db.SaveChangesAsync();
        return (factory.CreateClientWithToken(tokens.AdminToken), factory.CreateClient(), device.Id);
    }

    private static Task<HttpResponseMessage> SwitchAsync(HttpClient admin, Guid id, bool on) =>
        admin.PostAsJsonAsync($"/api/google-home/devices/{id}/switch", new { on });

    private static async Task<string?> LastValueAsync(Factory factory, Guid id)
    {
        using var scope = factory.Services.CreateScope();
        var device = await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().Devices.FindAsync(id);
        return device!.LastValue;
    }

    private static async Task AssertProblemAsync(HttpResponseMessage response, HttpStatusCode code, string text)
    {
        Assert.Equal(code, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.Contains(text, problem!.Title);
    }

    [Fact]
    public async Task Bridge_ohne_Schluessel_wird_abgelehnt()
    {
        using var factory = new Factory();
        using var client = factory.CreateClient();
        var response = await client.SendAsync(BridgeRequest("/api/google-home/bridge/poll", new { devices = Array.Empty<object>() }, key: null));
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Bridge_von_fremder_Adresse_wird_abgelehnt()
    {
        using var factory = new Factory();
        using var client = factory.CreateClient();
        var response = await client.SendAsync(BridgeRequest("/api/google-home/bridge/poll", new { devices = Array.Empty<object>() }, remoteIp: "192.0.2.10"));
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Nur_Admin_darf_schalten()
    {
        using var factory = new Factory();
        var (_, _, id) = await PrepareAsync(factory);
        var tokens = await factory.GetTokensAsync();
        using var user = factory.CreateClientWithToken(tokens.UserToken);
        Assert.Equal(HttpStatusCode.Forbidden, (await SwitchAsync(user, id, true)).StatusCode);
    }

    [Fact]
    public async Task Getrennte_App_meldet_verstaendlichen_Fehler()
    {
        using var factory = new Factory();
        var (admin, bridge, id) = await PrepareAsync(factory);

        await AssertProblemAsync(await SwitchAsync(admin, id, true), HttpStatusCode.ServiceUnavailable, "nicht verbunden");

        Assert.Empty(await PollAsync(bridge, Socket(false)));
        Assert.Equal("AUS", await LastValueAsync(factory, id));
    }

    [Fact]
    public async Task Ein_gilt_erst_nach_bestaetigtem_Geraetezustand()
    {
        using var factory = new Factory();
        var (admin, bridge, id) = await PrepareAsync(factory);
        await PollAsync(bridge, Socket(false));

        var pending = SwitchAsync(admin, id, true);
        var command = await WaitForCommandAsync(bridge, Socket(false));
        Assert.Equal(GoogleId, command.DeviceId);
        Assert.True(command.On);
        Assert.False(pending.IsCompleted);
        Assert.Equal("AUS", await LastValueAsync(factory, id));

        Assert.Equal(HttpStatusCode.OK, (await ReportAsync(bridge, command.Id, true)).StatusCode);

        Assert.Equal(HttpStatusCode.OK, (await pending).StatusCode);
        Assert.Equal("EIN", await LastValueAsync(factory, id));
    }

    [Fact]
    public async Task Abweichender_Zustand_gilt_nicht_als_Erfolg()
    {
        using var factory = new Factory();
        var (admin, bridge, id) = await PrepareAsync(factory);
        await PollAsync(bridge, Socket(false));

        var pending = SwitchAsync(admin, id, true);
        var command = await WaitForCommandAsync(bridge, Socket(false));
        await ReportAsync(bridge, command.Id, false);

        await AssertProblemAsync(await pending, HttpStatusCode.Conflict, "nicht bestätigt");
        Assert.Equal("AUS", await LastValueAsync(factory, id));
    }

    [Fact]
    public async Task Befehl_wird_genau_einmal_ausgeliefert()
    {
        using var factory = new Factory();
        var (admin, bridge, id) = await PrepareAsync(factory);
        await PollAsync(bridge, Socket(false));

        var pending = SwitchAsync(admin, id, true);
        var command = await WaitForCommandAsync(bridge, Socket(false));
        Assert.Empty(await PollAsync(bridge, Socket(false)));

        await ReportAsync(bridge, command.Id, true);
        await pending;
    }

    [Fact]
    public async Task Nicht_abgeholter_Befehl_wird_nach_Wiederverbindung_nie_ausgefuehrt()
    {
        using var factory = new Factory(pickupSeconds: 0.2);
        var (admin, bridge, id) = await PrepareAsync(factory);
        await PollAsync(bridge, Socket(false));

        await AssertProblemAsync(await SwitchAsync(admin, id, true), HttpStatusCode.GatewayTimeout, "nicht rechtzeitig");

        Assert.Empty(await PollAsync(bridge, Socket(false)));
        Assert.Equal("AUS", await LastValueAsync(factory, id));
    }

    [Fact]
    public async Task Verspaetete_Antwort_aendert_nichts()
    {
        using var factory = new Factory(pickupSeconds: 0.3, confirmSeconds: 0.6);
        var (admin, bridge, id) = await PrepareAsync(factory);
        await PollAsync(bridge, Socket(false));

        var pending = SwitchAsync(admin, id, true);
        var command = await WaitForCommandAsync(bridge, Socket(false));
        await AssertProblemAsync(await pending, HttpStatusCode.GatewayTimeout, "Zustand ist unbekannt");

        Assert.Equal(HttpStatusCode.NotFound, (await ReportAsync(bridge, command.Id, true)).StatusCode);
        Assert.Equal("AUS", await LastValueAsync(factory, id));
    }

    [Fact]
    public async Task Offline_Geraet_bekommt_keinen_Befehl()
    {
        using var factory = new Factory();
        var (admin, bridge, id) = await PrepareAsync(factory);
        await PollAsync(bridge, Socket(false, online: false));

        await AssertProblemAsync(await SwitchAsync(admin, id, true), HttpStatusCode.Conflict, "offline");
        Assert.Empty(await PollAsync(bridge, Socket(false, online: false)));
    }

    [Fact]
    public async Task Aenderung_aus_Google_Home_wird_uebernommen()
    {
        using var factory = new Factory();
        var (_, bridge, id) = await PrepareAsync(factory);

        await PollAsync(bridge, Socket(true));

        Assert.Equal("EIN", await LastValueAsync(factory, id));
    }

    [Fact]
    public async Task Alter_Tuya_Status_blockiert_und_ueberschreibt_nicht()
    {
        using var factory = new Factory();
        factory.Tuya.DeviceListResponse = _ => TuyaJson.DeviceList(false, null, (TuyaId, "Licht Wohnzimmer", false));
        factory.Tuya.StatusResponse = (_, _) => TuyaJson.Status(true);
        var (admin, bridge, id) = await PrepareAsync(factory);
        await PollAsync(bridge, Socket(false));

        (await admin.PostAsync("/api/tuya/sync", null)).EnsureSuccessStatusCode();
        Assert.Equal("AUS", await LastValueAsync(factory, id));

        var status = await admin.GetFromJsonAsync<StatusResponse>("/api/status");
        Assert.Equal(OnlineStates.Online, status!.Devices.Single(d => d.Id == id).State);

        await AssertProblemAsync(await admin.PostAsJsonAsync($"/api/tuya/devices/{id}/switch", new { on = true }),
            HttpStatusCode.Conflict, "Google Home");
        Assert.Empty(factory.Tuya.CommandBodies);
    }

    [Fact]
    public async Task Getrennte_App_ergibt_unbekannten_Status_statt_Tuya_Status()
    {
        using var factory = new Factory();
        factory.Tuya.DeviceListResponse = _ => TuyaJson.DeviceList(false, null, (TuyaId, "Licht Wohnzimmer", true));
        var (admin, _, id) = await PrepareAsync(factory);

        var status = await admin.GetFromJsonAsync<StatusResponse>("/api/status");

        Assert.Equal(OnlineStates.Unknown, status!.Devices.Single(d => d.Id == id).State);
    }

    [Fact]
    public async Task Tuya_Geraet_ohne_Verknuepfung_bleibt_ueber_Tuya_schaltbar()
    {
        using var factory = new Factory();
        factory.Tuya.DeviceListResponse = _ => TuyaJson.DeviceList(false, null, (TuyaId, "Heizdecke", true));
        factory.Tuya.StatusResponse = (number, _) => TuyaJson.Status(number > 1);
        var (admin, _, id) = await PrepareAsync(factory, "Heizdecke", googleId: null);

        var response = await admin.PostAsJsonAsync($"/api/tuya/devices/{id}/switch", new { on = true });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Single(factory.Tuya.CommandBodies);
    }

    private static async Task<HttpResponseMessage> LinkAsync(Factory factory, HttpClient admin, Guid id, string googleId)
    {
        using var scope = factory.Services.CreateScope();
        var device = await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().Devices.FindAsync(id);
        return await admin.PutAsJsonAsync($"/api/devices/{id}", new
        {
            name = device!.Name, type = device.Type, roomId = device.RoomId,
            tuyaDeviceId = device.TuyaDeviceId, googleHomeDeviceId = googleId,
        });
    }

    [Fact]
    public async Task Verknuepfung_uebernimmt_gemeldeten_Zustand_und_behaelt_Tuya_Id()
    {
        using var factory = new Factory();
        var (admin, bridge, id) = await PrepareAsync(factory, googleId: null);
        await PollAsync(bridge, Socket(true));

        var response = await LinkAsync(factory, admin, id, GoogleId);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var saved = await response.Content.ReadFromJsonAsync<DeviceResponse>();
        Assert.Equal(GoogleId, saved!.GoogleHomeDeviceId);
        Assert.Equal(TuyaId, saved.TuyaDeviceId);
        Assert.Equal("EIN", saved.LastValue);
    }

    [Fact]
    public async Task Nicht_gemeldetes_Geraet_laesst_sich_nicht_verknuepfen()
    {
        using var factory = new Factory();
        var (admin, _, id) = await PrepareAsync(factory, googleId: null);
        Assert.Equal(HttpStatusCode.BadRequest, (await LinkAsync(factory, admin, id, GoogleId)).StatusCode);
    }

    [Fact]
    public async Task Geraet_ohne_Ein_Aus_laesst_sich_nicht_verknuepfen()
    {
        using var factory = new Factory();
        var (admin, bridge, id) = await PrepareAsync(factory, googleId: null);
        await PollAsync(bridge, Socket(on: null));
        Assert.Equal(HttpStatusCode.BadRequest, (await LinkAsync(factory, admin, id, GoogleId)).StatusCode);
    }

    [Fact]
    public async Task PC_Steckdose_laesst_sich_nicht_verknuepfen()
    {
        using var factory = new Factory();
        var (admin, bridge, id) = await PrepareAsync(factory, googleId: null);
        await PollAsync(bridge, Socket(false, name: "Schlafzimmer"));

        Assert.Equal(HttpStatusCode.BadRequest, (await LinkAsync(factory, admin, id, GoogleId)).StatusCode);

        var devices = await admin.GetFromJsonAsync<JsonElement>("/api/google-home/devices");
        Assert.True(devices.GetProperty("devices")[0].GetProperty("protected").GetBoolean());
    }

    [Fact]
    public async Task PC_Steckdose_bekommt_auch_bei_vorhandener_Verknuepfung_keinen_Befehl()
    {
        using var factory = new Factory();
        var (admin, bridge, id) = await PrepareAsync(factory);
        await PollAsync(bridge, Socket(false, name: "Schlafzimmer"));

        await AssertProblemAsync(await SwitchAsync(admin, id, true), HttpStatusCode.UnprocessableEntity, "Messpunkt");

        Assert.Empty(await PollAsync(bridge, Socket(false, name: "Schlafzimmer")));
    }
}
