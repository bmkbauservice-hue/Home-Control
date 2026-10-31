using System.Net;
using System.Text;
using System.Text.Json;
using HomeControl.Api.Tests.Fakes;

namespace HomeControl.Api.Tests;

// Prüft die eingebaute Validierung von .NET 10 (AddValidation + DataAnnotations an LoginRequest und
// RegisterRequest): Ungültige Eingaben werden mit 400 und deutschen Meldungen abgelehnt, bevor der
// Endpunkt überhaupt läuft.
public class ValidationTests(HomeControlApiFactory factory) : IClassFixture<HomeControlApiFactory>
{
    private static StringContent Json(string json) => new(json, Encoding.UTF8, "application/json");

    // Alle Meldungen aus "errors" der ProblemDetails-Antwort. Über JsonDocument statt Textsuche, weil
    // System.Text.Json Umlaute im JSON als \u00FC schreibt ("gültig" stünde dort nicht wörtlich).
    private static async Task<List<string>> ErrorMessagesAsync(HttpResponseMessage response)
    {
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return json.RootElement.GetProperty("errors")
            .EnumerateObject()
            .SelectMany(field => field.Value.EnumerateArray())
            .Select(message => message.GetString() ?? string.Empty)
            .ToList();
    }

    [Fact]
    public async Task Login_ohne_Felder_gibt_400_mit_deutschen_Meldungen()
    {
        using var client = factory.CreateClient();

        var response = await client.PostAsync("/api/auth/login", Json("{}"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var messages = await ErrorMessagesAsync(response);
        Assert.Contains("Bitte eine E-Mail-Adresse eingeben.", messages);
        Assert.Contains("Bitte ein Passwort eingeben.", messages);
    }

    [Fact]
    public async Task Login_mit_ungueltiger_Email_gibt_400()
    {
        using var client = factory.CreateClient();

        var response = await client.PostAsync("/api/auth/login", Json("""{"email":"keine-mail","password":"x"}"""));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("Bitte eine gültige E-Mail-Adresse eingeben.", await ErrorMessagesAsync(response));
    }

    [Fact]
    public async Task Register_mit_unbekannter_Rolle_gibt_400()
    {
        using var client = factory.CreateClient();

        var response = await client.PostAsync(
            "/api/auth/register",
            Json("""{"email":"chef@example.de","password":"Passwort-123","role":"Chef"}"""));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains(await ErrorMessagesAsync(response), m => m.StartsWith("Rolle muss"));
    }
}
