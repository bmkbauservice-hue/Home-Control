using System.Net;
using System.Net.Http.Json;
using HomeControl.Api.Contracts;
using HomeControl.Api.Tests.Fakes;

namespace HomeControl.Api.Tests;

// Endpunkt-Tests mit der echten API (WebApplicationFactory): Schreibende Endpunkte nur für
// Admin, Lesen bleibt ohne Anmeldung möglich (Frontend und n8n-Agent).
// 401 = nicht angemeldet, 403 = angemeldet, aber kein Admin.
public class AdminOnlyEndpointTests(HomeControlApiFactory factory) : IClassFixture<HomeControlApiFactory>
{
    // Die Berechtigung wird vor dem Endpunkt geprüft. Ob Raum oder Gerät existiert, spielt
    // für 401 und 403 keine Rolle, deshalb reichen hier zufällige IDs.
    private static readonly Guid SomeId = Guid.NewGuid();

    public static TheoryData<string, string> WriteEndpoints => new()
    {
        { "POST", "/api/rooms" },
        { "PUT", $"/api/rooms/{SomeId}" },
        { "DELETE", $"/api/rooms/{SomeId}" },
        { "POST", "/api/devices" },
        { "PUT", $"/api/devices/{SomeId}" },
        { "DELETE", $"/api/devices/{SomeId}" },
        { "POST", $"/api/devices/{SomeId}/events" },
    };

    [Theory]
    [MemberData(nameof(WriteEndpoints))]
    public async Task Schreiben_ohne_Anmeldung_gibt_401(string method, string url)
    {
        using var client = factory.CreateClient();

        var response = await client.SendAsync(CreateRequest(method, url));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory]
    [MemberData(nameof(WriteEndpoints))]
    public async Task Schreiben_als_User_gibt_403(string method, string url)
    {
        var (_, userToken) = await factory.GetTokensAsync();
        using var client = factory.CreateClientWithToken(userToken);

        var response = await client.SendAsync(CreateRequest(method, url));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Theory]
    [MemberData(nameof(WriteEndpoints))]
    public async Task Schreiben_mit_ungueltigem_Token_gibt_401(string method, string url)
    {
        using var client = factory.CreateClientWithToken("kein.gueltiges.token");

        var response = await client.SendAsync(CreateRequest(method, url));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Tuya_Abgleich_ohne_Anmeldung_gibt_401()
    {
        using var client = factory.CreateClient();

        var response = await client.PostAsync("/api/tuya/test-device/sync", content: null);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory]
    [InlineData("/api/rooms")]
    [InlineData("/api/devices")]
    public async Task Lesen_geht_weiterhin_ohne_Anmeldung(string url)
    {
        using var client = factory.CreateClient();

        var response = await client.GetAsync(url);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Admin_darf_anlegen_aendern_und_loeschen()
    {
        var (adminToken, _) = await factory.GetTokensAsync();
        using var admin = factory.CreateClientWithToken(adminToken);

        var roomResponse = await admin.PostAsJsonAsync("/api/rooms", new RoomRequest("Testraum"));
        Assert.Equal(HttpStatusCode.Created, roomResponse.StatusCode);
        var room = await roomResponse.Content.ReadFromJsonAsync<RoomResponse>();

        var renameResponse = await admin.PutAsJsonAsync($"/api/rooms/{room!.Id}", new RoomRequest("Testraum neu"));
        Assert.Equal(HttpStatusCode.OK, renameResponse.StatusCode);

        var deviceResponse = await admin.PostAsJsonAsync(
            "/api/devices", new DeviceRequest("Testlampe", "Light", room.Id));
        Assert.Equal(HttpStatusCode.Created, deviceResponse.StatusCode);
        var device = await deviceResponse.Content.ReadFromJsonAsync<DeviceResponse>();

        var eventResponse = await admin.PostAsJsonAsync(
            $"/api/devices/{device!.Id}/events", new DeviceEventRequest("turned_on", null));
        Assert.Equal(HttpStatusCode.Created, eventResponse.StatusCode);

        var deleteDevice = await admin.DeleteAsync($"/api/devices/{device.Id}");
        Assert.Equal(HttpStatusCode.NoContent, deleteDevice.StatusCode);

        var deleteRoom = await admin.DeleteAsync($"/api/rooms/{room.Id}");
        Assert.Equal(HttpStatusCode.NoContent, deleteRoom.StatusCode);
    }

    // Mit gültigem JSON-Inhalt, damit eine 401 oder 403 wirklich von der Berechtigung kommt
    // und nicht von einer fehlenden Eingabe.
    private static HttpRequestMessage CreateRequest(string method, string url) =>
        new(new HttpMethod(method), url)
        {
            Content = method == "DELETE" ? null : JsonContent.Create(new { name = "Test", action = "turned_on" }),
        };
}
