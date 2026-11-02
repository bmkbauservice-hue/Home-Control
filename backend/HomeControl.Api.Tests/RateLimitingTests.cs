using System.Net;
using System.Net.Http.Json;
using HomeControl.Api.Contracts;
using HomeControl.Api.Tests.Fakes;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace HomeControl.Api.Tests;

// Rate Limiting (REVIEW.md M-2, M-3): N Versuche gehen durch, danach 429 mit Retry-After.
// Jeder Test baut seine eigene API, damit die Zähler bei 0 anfangen und sich nicht gegenseitig stören.
public class RateLimitingTests
{
    // Kleine Grenzen, damit die Tests schnell an die Grenze kommen. Kein echter Tuya-Zugang:
    // Die Tuya-Endpunkte antworten dann mit 503 "nicht eingerichtet", ohne ins Internet zu gehen.
    public class RateLimitApiFactory : HomeControlApiFactory
    {
        public const int LoginLimit = 3;
        public const int TuyaLimit = 2;

        protected override Dictionary<string, string?> Settings => new()
        {
            ["RateLimiting:Login:PermitLimit"] = LoginLimit.ToString(),
            ["RateLimiting:Login:WindowSeconds"] = "60",
            ["RateLimiting:Tuya:PermitLimit"] = TuyaLimit.ToString(),
            ["RateLimiting:Tuya:WindowSeconds"] = "60",
            ["Tuya:ClientId"] = "",
            ["Tuya:ClientSecret"] = "",
        };

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            // Wie hinter "tailscale serve": Die Verbindung kommt von 127.0.0.1 (siehe FakeRemoteIpStartupFilter).
            builder.ConfigureTestServices(services =>
                services.AddSingleton<IStartupFilter, FakeRemoteIpStartupFilter>());
        }
    }

    // Unsinnige Grenze: Die App soll gar nicht erst starten.
    private sealed class InvalidLimitFactory : HomeControlApiFactory
    {
        protected override Dictionary<string, string?> Settings => new()
        {
            ["RateLimiting:Login:PermitLimit"] = "0",
        };
    }

    // Unbekannte E-Mail: immer 401, löst aber keine Kontosperre aus (die würde den Test verfälschen).
    private static readonly LoginRequest WrongLogin = new("unbekannt@example.de", "Falsches-Passwort-1");

    // Anfrage mit X-Forwarded-For (wie von tailscale serve) und optional einer anderen Verbindungsadresse.
    private static HttpRequestMessage Request(HttpMethod method, string url, string? forwardedFor = null, string? remoteIp = null)
    {
        var request = new HttpRequestMessage(method, url);
        if (forwardedFor is not null)
        {
            request.Headers.Add("X-Forwarded-For", forwardedFor);
        }
        if (remoteIp is not null)
        {
            request.Headers.Add(FakeRemoteIpStartupFilter.HeaderName, remoteIp);
        }
        return request;
    }

    private static HttpRequestMessage LoginRequest(string? forwardedFor = null, string? remoteIp = null)
    {
        var request = Request(HttpMethod.Post, "/api/auth/login", forwardedFor, remoteIp);
        request.Content = JsonContent.Create(WrongLogin);
        return request;
    }

    // ---------- Regel "login" ----------

    [Fact]
    public async Task Login_nach_N_Versuchen_429_mit_Retry_After()
    {
        using var factory = new RateLimitApiFactory();
        using var client = factory.CreateClient();

        for (var i = 0; i < RateLimitApiFactory.LoginLimit; i++)
        {
            var allowed = await client.SendAsync(LoginRequest());
            Assert.Equal(HttpStatusCode.Unauthorized, allowed.StatusCode);
        }

        var blocked = await client.SendAsync(LoginRequest());

        Assert.Equal(HttpStatusCode.TooManyRequests, blocked.StatusCode);
        // Retry-After: ganze Sekunden bis zum nächsten Zeitfenster (höchstens die Fensterlänge).
        var retryAfter = int.Parse(Assert.Single(blocked.Headers.GetValues("Retry-After")));
        Assert.InRange(retryAfter, 1, 60);
        Assert.Equal("application/problem+json", blocked.Content.Headers.ContentType?.MediaType);
        var problem = await blocked.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.Equal(429, problem!.Status);
        Assert.Equal($"Zu viele Anmeldeversuche. Bitte in {retryAfter} Sekunden erneut versuchen.", problem.Title);
    }

    [Fact]
    public async Task Login_Register_und_Refresh_teilen_sich_einen_Zaehler()
    {
        using var factory = new RateLimitApiFactory();
        using var client = factory.CreateClient();

        // 2 × login + 1 × refresh = 3 = LoginLimit
        await client.SendAsync(LoginRequest());
        await client.SendAsync(LoginRequest());
        var refresh = await client.SendAsync(Request(HttpMethod.Post, "/api/auth/refresh"));
        Assert.Equal(HttpStatusCode.Unauthorized, refresh.StatusCode);

        // Die Registrierung kommt gar nicht mehr beim Endpunkt an.
        var register = await client.PostAsJsonAsync(
            "/api/auth/register", new RegisterRequest("neu@example.de", "Test-Passwort-123", null));

        Assert.Equal(HttpStatusCode.TooManyRequests, register.StatusCode);
    }

    [Fact]
    public async Task Anderes_Geraet_hat_ein_eigenes_Kontingent()
    {
        using var factory = new RateLimitApiFactory();
        using var client = factory.CreateClient();

        // Gerät A (Adresse kommt wie bei tailscale serve über X-Forwarded-For) verbraucht alles.
        for (var i = 0; i < RateLimitApiFactory.LoginLimit; i++)
        {
            await client.SendAsync(LoginRequest(forwardedFor: "100.64.0.1"));
        }
        var deviceA = await client.SendAsync(LoginRequest(forwardedFor: "100.64.0.1"));

        // Gerät B ist davon nicht betroffen.
        var deviceB = await client.SendAsync(LoginRequest(forwardedFor: "100.64.0.2"));

        Assert.Equal(HttpStatusCode.TooManyRequests, deviceA.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, deviceB.StatusCode);
    }

    [Fact]
    public async Task X_Forwarded_For_von_fremdem_Rechner_wird_ignoriert()
    {
        using var factory = new RateLimitApiFactory();
        using var client = factory.CreateClient();

        // Die Verbindung kommt NICHT von 127.0.0.1. Ein Angreifer schickt bei jedem Versuch eine
        // andere erfundene Adresse mit. Weil er kein bekannter Proxy ist, zählt seine echte Adresse.
        for (var i = 0; i < RateLimitApiFactory.LoginLimit; i++)
        {
            await client.SendAsync(LoginRequest(forwardedFor: $"100.64.1.{i}", remoteIp: "192.0.2.10"));
        }
        var blocked = await client.SendAsync(LoginRequest(forwardedFor: "100.64.1.99", remoteIp: "192.0.2.10"));

        Assert.Equal(HttpStatusCode.TooManyRequests, blocked.StatusCode);
    }

    // ---------- Regel "tuya" ----------

    [Fact]
    public async Task Tuya_hat_einen_gemeinsamen_Zaehler_fuer_alle_Geraete()
    {
        using var factory = new RateLimitApiFactory();
        using var client = factory.CreateClient();

        // Zwei Anfragen von Gerät A (Tuya ist im Test nicht eingerichtet → 503, zählt trotzdem) ...
        for (var i = 0; i < RateLimitApiFactory.TuyaLimit; i++)
        {
            var allowed = await client.SendAsync(Request(HttpMethod.Get, "/api/tuya/devices", forwardedFor: "100.64.0.1"));
            Assert.Equal(HttpStatusCode.ServiceUnavailable, allowed.StatusCode);
        }

        // ... und schon ist auch Gerät B gesperrt, auf einem anderen Tuya-Endpunkt.
        var blocked = await client.SendAsync(
            Request(HttpMethod.Get, "/api/tuya/test-device/status", forwardedFor: "100.64.0.2"));

        Assert.Equal(HttpStatusCode.TooManyRequests, blocked.StatusCode);
        Assert.True(blocked.Headers.Contains("Retry-After"));
        var problem = await blocked.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.StartsWith("Zu viele Anfragen an die Tuya-Cloud.", problem!.Title);
    }

    [Fact]
    public async Task Tuya_Anfragen_ohne_Anmeldung_verbrauchen_nichts()
    {
        using var factory = new RateLimitApiFactory();
        using var client = factory.CreateClient();

        // POST /api/tuya/sync verlangt eine Anmeldung. Die Autorisierung steht VOR dem Rate Limiter,
        // deshalb bekommt ein Fremder 401 und der gemeinsame Zähler bleibt unberührt.
        for (var i = 0; i < 5; i++)
        {
            var anonymous = await client.SendAsync(Request(HttpMethod.Post, "/api/tuya/sync"));
            Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
        }

        var stillAllowed = await client.SendAsync(Request(HttpMethod.Get, "/api/tuya/devices"));

        Assert.Equal(HttpStatusCode.ServiceUnavailable, stillAllowed.StatusCode);
    }

    [Fact]
    public async Task Status_hat_kein_Rate_Limit()
    {
        using var factory = new RateLimitApiFactory();
        using var client = factory.CreateClient();

        // Mehr Anfragen als die Tuya-Grenze erlaubt: Die Status-LEDs dürfen nie 429 bekommen.
        for (var i = 0; i < RateLimitApiFactory.TuyaLimit + 3; i++)
        {
            var response = await client.SendAsync(Request(HttpMethod.Get, "/api/status"));
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }
    }

    // ---------- Konfiguration ----------

    [Fact]
    public void Ungueltige_Grenze_verhindert_den_Start()
    {
        using var factory = new InvalidLimitFactory();

        var exception = Assert.ThrowsAny<Exception>(() => factory.CreateClient());

        Assert.Contains("RateLimiting", exception.ToString());
    }
}
