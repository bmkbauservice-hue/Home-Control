using System.Net;
using System.Net.Http.Json;
using HomeControl.Api.Contracts;
using HomeControl.Api.Tests.Fakes;

namespace HomeControl.Api.Tests;

// Endpunkt-Tests für /api/blink. Die Testfabrik läuft als Development: Dort ist Blink standardmäßig aus
// (appsettings.Development.json), damit kein zweiter Prozess das Token des Dienstes verbrennt.
public class BlinkEndpointTests(HomeControlApiFactory factory) : IClassFixture<HomeControlApiFactory>
{
    [Fact]
    public async Task In_der_Entwicklung_meldet_der_Status_disabled()
    {
        using var client = factory.CreateClient();

        var status = await client.GetFromJsonAsync<BlinkStatusResponse>("/api/blink/status");

        Assert.Equal("disabled", status!.State);
    }

    [Fact]
    public async Task In_der_Entwicklung_liefert_die_Kameraliste_503_mit_Erklaerung()
    {
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/api/blink/cameras");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Contains("ausgeschaltet", await response.Content.ReadAsStringAsync());
    }
}
