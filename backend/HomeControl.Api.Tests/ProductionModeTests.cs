using System.Net;
using System.Net.Http.Json;
using HomeControl.Api.Contracts;
using HomeControl.Api.Tests.Fakes;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace HomeControl.Api.Tests;

// Der Dauerdienst läuft als "Production" (REVIEW.md, M-1). Diese Tests prüfen, dass dort die
// sicheren Einstellungen greifen: Secure-Cookie, keine API-Doku, kein Start ohne JWT-Schlüssel.
public class ProductionModeTests(ProductionModeTests.ProductionApiFactory factory)
    : IClassFixture<ProductionModeTests.ProductionApiFactory>
{
    // Wie die normale Testfabrik, aber Umgebung Production und mit festem JWT-Schlüssel
    // (ohne Schlüssel würde die App im Production gar nicht starten, siehe letzter Test).
    public class ProductionApiFactory : HomeControlApiFactory
    {
        public const string SigningKey = "test-signaturschluessel-nur-fuer-tests-0123456789";

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            // Nach base aufgerufen: Die spätere Einstellung gewinnt.
            builder.UseEnvironment("Production");
            // UseSetting statt Settings: Program.cs liest den Schlüssel schon beim Aufbau
            // (builder.Configuration). Werte aus ConfigureAppConfiguration kommen bei Minimal APIs
            // erst später an, UseSetting dagegen sofort (wie UseEnvironment).
            builder.UseSetting("Jwt:SigningKey", SigningKey);
        }
    }

    // Production ohne JWT-Schlüssel: Die App soll lieber gar nicht starten.
    private sealed class ProductionWithoutKeyFactory : HomeControlApiFactory
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.UseEnvironment("Production");
        }
    }

    [Theory]
    [InlineData("/scalar")]
    [InlineData("/openapi/v1.json")]
    public async Task Api_Dokumentation_ist_aus(string url)
    {
        using var client = factory.CreateClient();

        var response = await client.GetAsync(url);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Refresh_Cookie_ist_Secure()
    {
        // Admin und User anlegen lassen, damit es ein Konto zum Anmelden gibt.
        await factory.GetTokensAsync();
        // HandleCookies = false: Der Client soll das Cookie nicht schlucken, wir wollen den Header sehen.
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });

        var response = await client.PostAsJsonAsync(
            "/api/auth/login",
            new LoginRequest(HomeControlApiFactory.AdminEmail, HomeControlApiFactory.Password));

        response.EnsureSuccessStatusCode();
        var cookie = Assert.Single(response.Headers.GetValues("Set-Cookie"), c => c.StartsWith("hc_refresh="));
        Assert.Contains("secure", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("httponly", cookie, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Ohne_JWT_Schluessel_startet_die_App_nicht()
    {
        using var withoutKey = new ProductionWithoutKeyFactory();

        var exception = Assert.ThrowsAny<Exception>(() => withoutKey.CreateClient());

        // Die Meldung nennt nur den Namen der Einstellung, nie einen Wert.
        Assert.Contains("Jwt:SigningKey", exception.ToString());
    }
}
