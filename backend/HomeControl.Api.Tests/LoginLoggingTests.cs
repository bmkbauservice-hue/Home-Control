using System.Net;
using System.Net.Http.Json;
using HomeControl.Api.Contracts;
using HomeControl.Api.Tests.Fakes;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Logging;

namespace HomeControl.Api.Tests;

// Fehlgeschlagene Logins landen mit Warnstufe im Log (REVIEW.md M-3, N-9), aber ohne Passwort und
// ohne die eingegebene E-Mail-Adresse.
public class LoginLoggingTests
{
    private sealed class LoggingApiFactory : HomeControlApiFactory
    {
        public CapturingLoggerProvider Logs { get; } = new();

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.ConfigureLogging(logging => logging.AddProvider(Logs));
        }

        // Nur die Zeilen zu fehlgeschlagenen Anmeldungen.
        public List<(LogLevel Level, string Message)> FailedLogins() =>
            Logs.Entries.Where(e => e.Message.StartsWith("Anmeldung fehlgeschlagen")).ToList();
    }

    [Fact]
    public async Task Falsches_Passwort_wird_ohne_Passwort_und_Email_geloggt()
    {
        using var factory = new LoggingApiFactory();
        await factory.GetTokensAsync();
        using var client = factory.CreateClient();
        const string wrongPassword = "Geheim-Falsch-987";

        var response = await client.PostAsJsonAsync("/api/auth/login",
            new LoginRequest(HomeControlApiFactory.AdminEmail, wrongPassword));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        var entry = Assert.Single(factory.FailedLogins());
        Assert.Equal(LogLevel.Warning, entry.Level);
        Assert.Contains("falsche Zugangsdaten", entry.Message);
        Assert.DoesNotContain(wrongPassword, entry.Message);
        Assert.DoesNotContain(HomeControlApiFactory.AdminEmail, entry.Message);
        Assert.DoesNotContain("Benutzer unbekannt", entry.Message);
    }

    [Fact]
    public async Task Unbekannte_Email_wird_als_unbekannter_Benutzer_geloggt()
    {
        using var factory = new LoggingApiFactory();
        using var client = factory.CreateClient();

        await client.PostAsJsonAsync("/api/auth/login", new LoginRequest("niemand@example.de", "Egal-Passwort-1"));

        var entry = Assert.Single(factory.FailedLogins());
        Assert.Equal(LogLevel.Warning, entry.Level);
        Assert.Contains("Benutzer unbekannt", entry.Message);
        Assert.DoesNotContain("niemand@example.de", entry.Message);
    }

    [Fact]
    public async Task Erfolgreicher_Login_erzeugt_keine_Warnung()
    {
        using var factory = new LoggingApiFactory();
        await factory.GetTokensAsync();

        Assert.Empty(factory.FailedLogins());
    }
}
