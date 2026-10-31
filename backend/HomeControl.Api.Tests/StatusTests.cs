using System.Net;
using System.Net.Http.Json;
using HomeControl.Api.Contracts;
using HomeControl.Api.Status;
using HomeControl.Api.Tests.Fakes;
using HomeControl.Api.Tuya;

namespace HomeControl.Api.Tests;

// Tests für die Status-LEDs: Welche Farbe bekommt ein Gerät, wie lange merkt sich der Server die
// Tuya-Antwort, und liefert GET /api/status eine Antwort. Keine echte Anfrage an Tuya.
public class StatusTests
{
    private static readonly Guid LampeId = Guid.NewGuid();
    private static readonly Guid SteckdoseId = Guid.NewGuid();
    private static readonly Guid SensorId = Guid.NewGuid();
    private static readonly Guid OhneTuyaId = Guid.NewGuid();

    private static readonly DeviceLink[] Devices =
    [
        new(LampeId, "Lampe", "tuya-lampe"),
        new(SteckdoseId, "Steckdose", "tuya-steckdose"),
        new(SensorId, "Sensor", "tuya-sensor"),
        new(OhneTuyaId, "Ohne Tuya", null),
    ];

    private static string StateOf(List<DeviceOnlineState> states, Guid id) => states.Single(s => s.Id == id).State;

    // ---------- Welche Farbe? ----------

    [Fact]
    public void Online_offline_und_unbekannt_werden_richtig_zugeordnet()
    {
        var cloud = new Dictionary<string, bool?>
        {
            ["tuya-lampe"] = true,
            ["tuya-steckdose"] = false,
            ["tuya-sensor"] = null,
        };

        var states = DeviceOnlineEvaluator.Evaluate(Devices, cloud);

        Assert.Equal(OnlineStates.Online, StateOf(states, LampeId));
        Assert.Equal(OnlineStates.Offline, StateOf(states, SteckdoseId));
        Assert.Equal(OnlineStates.Unknown, StateOf(states, SensorId));
        Assert.Equal(OnlineStates.Unknown, StateOf(states, OhneTuyaId));
    }

    [Fact]
    public void Ohne_Daten_aus_der_Cloud_ist_alles_unbekannt()
    {
        var states = DeviceOnlineEvaluator.Evaluate(Devices, cloudOnline: null);

        Assert.All(states, s => Assert.Equal(OnlineStates.Unknown, s.State));
    }

    [Fact]
    public void Verknuepftes_Geraet_fehlt_in_der_Cloud_ist_unbekannt()
    {
        var states = DeviceOnlineEvaluator.Evaluate(Devices, new Dictionary<string, bool?>());

        Assert.Equal(OnlineStates.Unknown, StateOf(states, LampeId));
    }

    // ---------- Zwischenspeicher ----------

    private int loadCalls;

    private Task<List<TuyaCloudDevice>> LoadAsync(CancellationToken _)
    {
        loadCalls++;
        return Task.FromResult(new List<TuyaCloudDevice> { new("tuya-lampe", "Lampe", "Lampe", "dj", true) });
    }

    [Fact]
    public async Task Cache_fragt_innerhalb_von_60_Sekunden_nur_einmal_bei_Tuya()
    {
        var time = new FakeTimeProvider();
        var cache = new TuyaOnlineCache(time);

        await cache.GetAsync(LoadAsync, CancellationToken.None);
        time.Advance(TimeSpan.FromSeconds(59));
        var second = await cache.GetAsync(LoadAsync, CancellationToken.None);

        Assert.Equal(1, loadCalls);
        Assert.True(second["tuya-lampe"]);
    }

    [Fact]
    public async Task Cache_fragt_nach_60_Sekunden_neu()
    {
        var time = new FakeTimeProvider();
        var cache = new TuyaOnlineCache(time);

        await cache.GetAsync(LoadAsync, CancellationToken.None);
        time.Advance(TimeSpan.FromSeconds(60));
        await cache.GetAsync(LoadAsync, CancellationToken.None);

        Assert.Equal(2, loadCalls);
    }

    [Fact]
    public async Task Fehler_wird_nicht_gespeichert()
    {
        var cache = new TuyaOnlineCache(new FakeTimeProvider());

        await Assert.ThrowsAsync<HttpRequestException>(() =>
            cache.GetAsync(_ => throw new HttpRequestException("Cloud weg"), CancellationToken.None));
        var result = await cache.GetAsync(LoadAsync, CancellationToken.None);

        Assert.Equal(1, loadCalls);
        Assert.True(result["tuya-lampe"]);
    }
}

// Endpunkt-Test mit der echten API. Tuya wird ausdrücklich abgeschaltet (leere Zugangsdaten):
// Die Tests laufen im Development und würden sonst die echten User Secrets und damit die echte
// Tuya-Cloud benutzen.
public class StatusEndpointTests(StatusEndpointTests.NoTuyaApiFactory factory)
    : IClassFixture<StatusEndpointTests.NoTuyaApiFactory>
{
    public class NoTuyaApiFactory : HomeControlApiFactory
    {
        protected override Dictionary<string, string?> Settings => new()
        {
            ["Tuya:ClientId"] = "",
            ["Tuya:ClientSecret"] = "",
        };
    }

    [Fact]
    public async Task Status_ohne_Tuya_meldet_notConfigured()
    {
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/api/status");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<StatusResponse>();
        Assert.NotNull(body);
        Assert.Equal(OnlineStates.NotConfigured, body.Tuya);
        Assert.Equal(0, body.DevicesOnline);
        Assert.Equal(0, body.DevicesOffline);
        Assert.Equal(body.Devices.Count, body.DevicesUnknown);
    }
}
