using System.Net;
using System.Net.Http.Json;
using HomeControl.Api.Contracts;
using HomeControl.Api.Tests.Fakes;

namespace HomeControl.Api.Tests;

// Eigener Abgleich-Takt pro Gerät (Device.TuyaSyncSeconds): nur feste Werte (3, 5, 15 Minuten oder Standard),
// und nur bei einer Tuya-Verknüpfung. Jeder kürzere Takt kostet Tuya-Kontingent, deshalb keine beliebigen Zahlen.
public class TuyaSyncSettingTests(HomeControlApiFactory factory) : IClassFixture<HomeControlApiFactory>
{
    private async Task<(HttpClient Admin, Guid RoomId)> PrepareAsync()
    {
        var (adminToken, _) = await factory.GetTokensAsync();
        var admin = factory.CreateClientWithToken(adminToken);
        var room = await (await admin.PostAsJsonAsync("/api/rooms", new RoomRequest($"Flur {Guid.NewGuid():N}")))
            .Content.ReadFromJsonAsync<RoomResponse>();
        return (admin, room!.Id);
    }

    // Tuya-Ids bestehen nur aus Buchstaben und Ziffern; je Test eine eigene, weil jede nur einmal verknüpft sein darf.
    private static string NewTuyaId() => $"tuya{Guid.NewGuid():N}"[..20];

    [Fact]
    public async Task Tuer_mit_3_Minuten_wird_gespeichert()
    {
        var (admin, roomId) = await PrepareAsync();

        var response = await admin.PostAsJsonAsync("/api/devices",
            new DeviceRequest("Tür", "DoorSensor", roomId, NewTuyaId(), TuyaSyncSeconds: 180));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal(180, (await response.Content.ReadFromJsonAsync<DeviceResponse>())!.TuyaSyncSeconds);
    }

    [Fact]
    public async Task Beliebiger_Takt_wird_abgelehnt()
    {
        var (admin, roomId) = await PrepareAsync();

        var response = await admin.PostAsJsonAsync("/api/devices",
            new DeviceRequest("Tür", "DoorSensor", roomId, NewTuyaId(), TuyaSyncSeconds: 10));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("tuyaSyncSeconds", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Ohne_Tuya_Verknuepfung_bleibt_der_Takt_leer()
    {
        var (admin, roomId) = await PrepareAsync();

        var response = await admin.PostAsJsonAsync("/api/devices",
            new DeviceRequest("Lampe", "Light", roomId, TuyaDeviceId: null, TuyaSyncSeconds: 180));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Null((await response.Content.ReadFromJsonAsync<DeviceResponse>())!.TuyaSyncSeconds);
    }
}
