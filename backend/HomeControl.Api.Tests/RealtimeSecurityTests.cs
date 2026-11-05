using System.Net;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using HomeControl.Api.Auth;
using HomeControl.Api.Models;
using HomeControl.Api.Realtime;
using HomeControl.Api.Tests.Fakes;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace HomeControl.Api.Tests;

// Sicherheit des Live-Kanals GET /api/live/devices am echten Endpunkt: Anmeldung, Rollen, Tokenablauf, Ressourcen.
public class RealtimeSecurityTests
{
    private const string Url = "/api/live/devices";

    // Baut ein JWT mit frei gewähltem Ablauf, wie es TokenService ausstellen würde.
    private static string CreateToken(HomeControlApiFactory factory, DateTimeOffset expires, string? otherKey = null)
    {
        var options = factory.Services.GetRequiredService<JwtOptions>();
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(otherKey ?? options.SigningKey));
        return new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = options.Issuer,
            Audience = options.Audience,
            NotBefore = expires.UtcDateTime.AddHours(-1),
            IssuedAt = expires.UtcDateTime.AddHours(-1),
            Expires = expires.UtcDateTime,
            Subject = new ClaimsIdentity(
            [
                new Claim(JwtRegisteredClaimNames.Sub, Guid.NewGuid().ToString()),
                new Claim(JwtRegisteredClaimNames.Email, "test@example.de"),
                new Claim(TokenService.RoleClaim, Roles.User),
            ]),
            SigningCredentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256),
        });
    }

    private static Task<HttpResponseMessage> OpenAsync(HttpClient client, CancellationToken cancellationToken = default) =>
        client.GetAsync(Url, HttpCompletionOption.ResponseHeadersRead, cancellationToken);

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        for (var i = 0; i < 100 && !condition(); i++)
        {
            await Task.Delay(50);
        }

        Assert.True(condition());
    }

    // ---------- Anmeldung ----------

    [Fact]
    public async Task Token_in_der_Adresse_wird_nicht_akzeptiert()
    {
        using var factory = new HomeControlApiFactory();
        var (adminToken, _) = await factory.GetTokensAsync();
        using var client = factory.CreateClient();

        var response = await client.GetAsync($"{Url}?access_token={adminToken}");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Token_mit_falscher_Signatur_wird_abgelehnt()
    {
        using var factory = new HomeControlApiFactory();
        var forged = CreateToken(factory, DateTimeOffset.UtcNow.AddMinutes(10), new string('x', 64));
        using var client = factory.CreateClientWithToken(forged);

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync(Url)).StatusCode);
    }

    // Lesen darf jeder Angemeldete, also auch der Kanal. Er enthält nichts, was die REST-Endpunkte nicht auch zeigen.
    [Fact]
    public async Task Normale_Benutzer_duerfen_mitlesen_und_die_Antwort_wird_nicht_zwischengespeichert()
    {
        using var factory = new HomeControlApiFactory();
        var (_, userToken) = await factory.GetTokensAsync();
        using var user = factory.CreateClientWithToken(userToken);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        using var response = await OpenAsync(user, timeout.Token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/event-stream", response.Content.Headers.ContentType?.MediaType);
        Assert.True(response.Headers.CacheControl?.NoStore == true || response.Headers.CacheControl?.NoCache == true);
    }

    // ---------- Tokenablauf ----------

    [Theory]
    [InlineData(-10)]   // abgelaufen, aber im 30-s-Spielraum der JWT-Prüfung: Der Endpunkt lehnt selbst ab.
    [InlineData(-120)]  // klar abgelaufen: Schon die JWT-Prüfung lehnt ab.
    public async Task Abgelaufenes_Token_bekommt_401_und_keinen_Strom(int secondsAgo)
    {
        using var factory = new HomeControlApiFactory();
        using var client = factory.CreateClientWithToken(CreateToken(factory, DateTimeOffset.UtcNow.AddSeconds(secondsAgo)));

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync(Url)).StatusCode);
        Assert.Equal(0, factory.Services.GetRequiredService<DeviceChangeHub>().SubscriberCount);
    }

    // Weist nach, dass der exp-Claim am Endpunkt wirklich ankommt: Ohne ihn liefe der Strom 15 Minuten.
    [Fact]
    public async Task Strom_endet_beim_Ablauf_des_Tokens_und_gibt_den_Platz_frei()
    {
        using var factory = new HomeControlApiFactory();
        using var client = factory.CreateClientWithToken(CreateToken(factory, DateTimeOffset.UtcNow.AddSeconds(3)));
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));

        using var response = await OpenAsync(client, timeout.Token);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync(timeout.Token);

        Assert.Contains("event: connected", body);
        await WaitUntilAsync(() => factory.Services.GetRequiredService<DeviceChangeHub>().SubscriberCount == 0);
    }

    // ---------- Ressourcen ----------

    [Fact]
    public async Task Trennt_der_Browser_die_Verbindung_wird_der_Platz_frei()
    {
        using var factory = new HomeControlApiFactory();
        var (adminToken, _) = await factory.GetTokensAsync();
        using var admin = factory.CreateClientWithToken(adminToken);
        var hub = factory.Services.GetRequiredService<DeviceChangeHub>();

        using (var disconnect = new CancellationTokenSource())
        {
            var response = await OpenAsync(admin, disconnect.Token);
            await WaitUntilAsync(() => hub.SubscriberCount == 1);
            await disconnect.CancelAsync();
            response.Dispose();
        }

        await WaitUntilAsync(() => hub.SubscriberCount == 0);
    }

    [Fact]
    public async Task Sind_alle_Plaetze_belegt_gibt_es_503_und_danach_wieder_einen_Platz()
    {
        using var factory = new HomeControlApiFactory();
        var (adminToken, _) = await factory.GetTokensAsync();
        using var admin = factory.CreateClientWithToken(adminToken);
        var hub = factory.Services.GetRequiredService<DeviceChangeHub>();
        var occupied = Enumerable.Range(0, DeviceChangeHub.MaxSubscribers).Select(_ => hub.TrySubscribe()!).ToList();

        var full = await admin.GetAsync(Url);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, full.StatusCode);

        occupied[0].Dispose();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        using var again = await OpenAsync(admin, timeout.Token);
        Assert.Equal(HttpStatusCode.OK, again.StatusCode);
    }

    // ---------- Inhalt ----------

    [Fact]
    public void Ereignis_enthaelt_nur_Ids_Quelle_und_Zeit()
    {
        var change = new DeviceChangeEvent([Guid.NewGuid()], DeviceChangeSources.TuyaSync, DateTimeOffset.UnixEpoch);

        var json = JsonSerializer.SerializeToElement(change, new JsonSerializerOptions(JsonSerializerDefaults.Web));

        Assert.Equal(["deviceIds", "source", "atUtc"], json.EnumerateObject().Select(p => p.Name));
    }
}
