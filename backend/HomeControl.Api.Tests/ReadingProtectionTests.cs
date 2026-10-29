using System.Net;
using System.Net.Http.Json;
using HomeControl.Api.Contracts;
using HomeControl.Api.Tests.Fakes;

namespace HomeControl.Api.Tests;

// Mit eingeschaltetem "Auth:RequireLoginForReading" verlangt auch Lesen eine Anmeldung:
// per JWT (Frontend) oder per API-Schlüssel (KI-Agent). Der Schlüssel erlaubt nur Lesen.
public class ReadingProtectionTests(LoginRequiredApiFactory factory) : IClassFixture<LoginRequiredApiFactory>
{
    [Theory]
    [InlineData("/api/rooms")]
    [InlineData("/api/devices")]
    public async Task Lesen_ohne_Anmeldung_gibt_401(string url)
    {
        using var client = factory.CreateClient();

        var response = await client.GetAsync(url);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory]
    [InlineData("/api/rooms")]
    [InlineData("/api/devices")]
    public async Task Lesen_mit_API_Schluessel_klappt(string url)
    {
        using var client = factory.CreateClientWithApiKey(LoginRequiredApiFactory.AgentApiKey);

        var response = await client.GetAsync(url);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Falscher_API_Schluessel_gibt_401()
    {
        using var client = factory.CreateClientWithApiKey("das-ist-der-falsche-schluessel-0123456789");

        var response = await client.GetAsync("/api/rooms");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task API_Schluessel_darf_nicht_schreiben()
    {
        using var client = factory.CreateClientWithApiKey(LoginRequiredApiFactory.AgentApiKey);

        var response = await client.PostAsJsonAsync("/api/rooms", new RoomRequest("Agent-Raum"));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Lesen_mit_Benutzer_Token_klappt()
    {
        var (_, userToken) = await factory.GetTokensAsync();
        using var client = factory.CreateClientWithToken(userToken);

        var response = await client.GetAsync("/api/rooms");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Anmeldung_bleibt_ohne_Token_moeglich()
    {
        // GetTokensAsync registriert und meldet an, ganz ohne Token. Klappt das, sind
        // register und login trotz Fallback-Regel offen.
        var (adminToken, _) = await factory.GetTokensAsync();

        Assert.False(string.IsNullOrEmpty(adminToken));
    }
}
