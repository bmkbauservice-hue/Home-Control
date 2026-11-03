using System.Net;
using System.Net.Http.Json;
using HomeControl.Api.Contracts;
using HomeControl.Api.Tests.Fakes;

namespace HomeControl.Api.Tests;

// GET /api/auth/setup sagt der Anmeldeseite, ob sie "Erstes Konto anlegen" zeigen soll.
public class SetupEndpointTests
{
    [Fact]
    public async Task Ohne_Benutzer_braucht_es_das_erste_Konto()
    {
        // Eigene Fabrik = eigene, leere Datenbank.
        using var factory = new HomeControlApiFactory();
        using var client = factory.CreateClient();

        var setup = await client.GetFromJsonAsync<SetupResponse>("/api/auth/setup");

        Assert.True(setup!.NeedsFirstAccount);
    }

    [Fact]
    public async Task Mit_Benutzer_nicht_mehr()
    {
        using var factory = new HomeControlApiFactory();
        await factory.GetTokensAsync(); // legt Admin und Benutzer an
        using var client = factory.CreateClient();

        var setup = await client.GetFromJsonAsync<SetupResponse>("/api/auth/setup");

        Assert.False(setup!.NeedsFirstAccount);
    }

    [Fact]
    public async Task Bleibt_mit_Leseschutz_ohne_Anmeldung_erreichbar()
    {
        // Die Anmeldeseite fragt, bevor jemand angemeldet ist.
        using var factory = new LoginRequiredApiFactory();
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/api/auth/setup");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
