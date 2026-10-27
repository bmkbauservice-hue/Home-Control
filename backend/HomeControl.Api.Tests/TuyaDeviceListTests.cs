using System.Text.Json;
using HomeControl.Api.Tests.Fakes;
using HomeControl.Api.Tuya;

namespace HomeControl.Api.Tests;

// Tests für das Abrufen der Geräteliste aus der Tuya-Cloud (TuyaClient.GetLinkedAppDevicesAsync).
// Es geht keine Anfrage ins Internet: FakeTuyaHandler spielt die Tuya-Cloud.
public class TuyaDeviceListTests
{
    private readonly FakeTuyaHandler handler = new();
    private readonly FakeTimeProvider time = new();
    private readonly TestOptionsMonitor<TuyaOptions> options = new(new TuyaOptions
    {
        ClientId = "test-client",
        ClientSecret = "test-secret",
    });

    private Task<List<TuyaCloudDevice>> GetDevicesAsync()
    {
        var http = new HttpClient(handler, disposeHandler: false) { BaseAddress = new Uri("https://openapi.tuyaeu.com") };
        var client = new TuyaClient(http, options, new TuyaTokenStore(time), time);
        return client.GetLinkedAppDevicesAsync(CancellationToken.None);
    }

    [Fact]
    public async Task EineSeite_LiefertGeraeteMitNamenUndOnlineStatus()
    {
        handler.DeviceListResponse = _ => TuyaJson.DeviceList(false, null, ("abc1", "Bad Licht", true), ("abc2", "Barlicht", false));

        var devices = await GetDevicesAsync();

        Assert.Equal(2, devices.Count);
        Assert.Equal(new TuyaCloudDevice("abc1", "Bad Licht", "Wi-Fi Socket", "cz", true), devices[0]);
        Assert.False(devices[1].Online);
        Assert.Equal(["/v1.0/iot-01/associated-users/devices?size=50"], handler.DeviceListPaths);
    }

    [Fact]
    public async Task LocalKey_WirdNichtUebernommen()
    {
        handler.DeviceListResponse = _ => TuyaJson.DeviceList(false, null, ("abc1", "Bad Licht", true));

        var devices = await GetDevicesAsync();

        // Der local_key steht in der Tuya-Antwort, darf aber nirgends im Ergebnis auftauchen.
        Assert.DoesNotContain("geheim", JsonSerializer.Serialize(devices));
    }

    [Fact]
    public async Task MehrereSeiten_WerdenZusammengefuegt()
    {
        handler.DeviceListResponse = page => page == 1
            ? TuyaJson.DeviceList(true, "seite2", ("abc1", "Bad Licht", true))
            : TuyaJson.DeviceList(false, null, ("abc2", "Barlicht", true));

        var devices = await GetDevicesAsync();

        Assert.Equal(new[] { "abc1", "abc2" }, devices.Select(d => d.Id));
        // Zweite Seite mit last_row_key, Query-Parameter alphabetisch sortiert (wichtig für die Signatur)
        Assert.Equal(
            ["/v1.0/iot-01/associated-users/devices?size=50", "/v1.0/iot-01/associated-users/devices?last_row_key=seite2&size=50"],
            handler.DeviceListPaths);
    }

    [Fact]
    public async Task EndloseSeiten_BrechenNach20Abrufen()
    {
        // Tuya meldet immer "es gibt noch mehr". Der Client muss trotzdem aufhören.
        handler.DeviceListResponse = page => TuyaJson.DeviceList(true, $"seite{page + 1}", ($"id{page}", $"Gerät {page}", true));

        var devices = await GetDevicesAsync();

        Assert.Equal(20, handler.DeviceListPaths.Count);
        Assert.Equal(20, devices.Count);
    }

    [Fact]
    public async Task OhneGeraete_LiefertLeereListe()
    {
        var devices = await GetDevicesAsync();

        Assert.Empty(devices);
    }

    [Fact]
    public async Task Tuyafehler_WirdAlsTuyaApiExceptionWeitergegeben()
    {
        handler.DeviceListResponse = _ => TuyaJson.Error("1106", "permission deny");

        var ex = await Assert.ThrowsAsync<TuyaApiException>(GetDevicesAsync);

        Assert.Equal("1106", ex.TuyaCode);
    }
}
