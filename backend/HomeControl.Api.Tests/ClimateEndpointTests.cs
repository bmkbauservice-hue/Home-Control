using System.Net;
using System.Net.Http.Json;
using HomeControl.Api.Climate;
using HomeControl.Api.Tests.Fakes;

namespace HomeControl.Api.Tests;

// Echte ASP.NET-Endpunkte gegen isolierte SQLite-Testdatenbanken, ohne Android oder Cloud.
public class ClimateEndpointTests(HomeControlApiFactory factory)
    : IClassFixture<HomeControlApiFactory>
{
    [Theory]
    [InlineData("/api/climate")]
    [InlineData("/api/climate/bridge/sensors")]
    [InlineData("/api/climate/bridge/status")]
    public async Task Klimadaten_erfordern_Anmeldung(string endpoint)
    {
        using var client = factory.CreateClient();
        var response = await client.GetAsync(endpoint);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Klima_ohne_Sensoren_zeigt_keine_erfundenen_Werte()
    {
        var (_, userToken) = await factory.GetTokensAsync();
        using var user = factory.CreateClientWithToken(userToken);

        var readings = await user.GetAsync("/api/climate");
        Assert.Equal(HttpStatusCode.OK, readings.StatusCode);
        var values = await readings.Content.ReadFromJsonAsync<List<ClimateEndpoints.RoomClimateResponse>>();
        Assert.Empty(values!);

        var sensors = await user.GetAsync("/api/climate/bridge/sensors");
        Assert.Equal(HttpStatusCode.OK, sensors.StatusCode);
        var detected = await sensors.Content.ReadFromJsonAsync<List<ClimateEndpoints.SensorResponse>>();
        Assert.Empty(detected!);
    }

    [Fact]
    public async Task Nur_Admin_darf_Sensoren_Raeumen_zuordnen()
    {
        var (_, userToken) = await factory.GetTokensAsync();
        using var user = factory.CreateClientWithToken(userToken);

        var response = await user.PutAsJsonAsync(
            "/api/climate/bridge/sensors/does-not-exist/room", new { roomId = (Guid?)null });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Nicht_eingerichtete_Bridge_meldet_Status_ohne_Schluessel()
    {
        var (adminToken, _) = await factory.GetTokensAsync();
        using var admin = factory.CreateClientWithToken(adminToken);

        var response = await admin.GetAsync("/api/climate/bridge/status");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var status = await response.Content.ReadFromJsonAsync<ClimateStatus>();
        Assert.NotNull(status);
        Assert.False(status.BridgeConfigured);
        Assert.Equal(0, status.Sensors);
        Assert.Equal(0, status.Assigned);
        Assert.False(status.HasFreshReadings);
    }

    public record ClimateStatus(bool BridgeConfigured, int Sensors, int Assigned,
        DateTimeOffset? LastReceivedAtUtc, bool HasFreshReadings);
}
