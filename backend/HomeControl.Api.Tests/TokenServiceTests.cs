using HomeControl.Api.Auth;
using HomeControl.Api.Models;
using HomeControl.Api.Tests.Fakes;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace HomeControl.Api.Tests;

// Tests für TokenService: Inhalt und Laufzeit des JWT, Signaturprüfung, Ablauf,
// Refresh-Token und dessen Hash. Kein Webserver, keine Datenbank.
public class TokenServiceTests
{
    private static readonly JwtOptions Options = new()
    {
        Issuer = "HomeControl",
        Audience = "HomeControl",
        SigningKey = "test-schluessel-nur-fuer-unit-tests-mindestens-32-zeichen",
        AccessTokenMinutes = 15,
    };

    private static readonly User TestUser = new()
    {
        Id = Guid.Parse("11111111-2222-3333-4444-555555555555"),
        Email = "mario@example.de",
        PasswordHash = "darf-nie-im-token-stehen",
        Role = Roles.User,
    };

    private static Task<TokenValidationResult> ValidateAsync(string token, JwtOptions options) =>
        new JsonWebTokenHandler().ValidateTokenAsync(token, TokenService.CreateValidationParameters(options));

    [Fact]
    public async Task CreateAccessToken_ist_gueltig_und_enthaelt_nur_Id_Email_und_Rolle()
    {
        var service = new TokenService(Options, TimeProvider.System);

        var accessToken = service.CreateAccessToken(TestUser);
        var result = await ValidateAsync(accessToken.Token, Options);

        Assert.True(result.IsValid);
        Assert.Equal(TestUser.Id.ToString(), result.ClaimsIdentity.FindFirst(JwtRegisteredClaimNames.Sub)?.Value);
        Assert.Equal("mario@example.de", result.ClaimsIdentity.FindFirst(JwtRegisteredClaimNames.Email)?.Value);
        Assert.Equal(Roles.User, result.ClaimsIdentity.FindFirst(TokenService.RoleClaim)?.Value);
        // Der Passwort-Hash darf nicht im Token stehen: Ein JWT ist nur signiert, nicht verschlüsselt.
        Assert.DoesNotContain("darf-nie-im-token-stehen", Base64UrlEncoder.Decode(accessToken.Token.Split('.')[1]));
    }

    [Fact]
    public void CreateAccessToken_ist_15_Minuten_gueltig()
    {
        var time = new FakeTimeProvider();
        var service = new TokenService(Options, time);

        var accessToken = service.CreateAccessToken(TestUser);
        var jwt = new JsonWebToken(accessToken.Token);

        Assert.Equal(time.GetUtcNow().AddMinutes(15), accessToken.ExpiresAtUtc);
        Assert.Equal(TimeSpan.FromMinutes(15), jwt.ValidTo - jwt.IssuedAt);
    }

    [Fact]
    public async Task Token_mit_anderem_Schluessel_ist_ungueltig()
    {
        var service = new TokenService(Options, TimeProvider.System);
        var otherOptions = new JwtOptions
        {
            Issuer = Options.Issuer,
            Audience = Options.Audience,
            SigningKey = "ein-ganz-anderer-schluessel-mit-mindestens-32-zeichen",
        };

        var accessToken = service.CreateAccessToken(TestUser);
        var result = await ValidateAsync(accessToken.Token, otherOptions);

        Assert.False(result.IsValid);
    }

    [Fact]
    public async Task Veraendertes_Token_wird_abgelehnt()
    {
        // Angriff: Rolle im Token von "User" auf "Admin" ändern, Signatur unverändert lassen.
        var service = new TokenService(Options, TimeProvider.System);
        var parts = service.CreateAccessToken(TestUser).Token.Split('.');
        var payload = Base64UrlEncoder.Decode(parts[1]);
        Assert.Contains("\"role\":\"User\"", payload);

        parts[1] = Base64UrlEncoder.Encode(payload.Replace("\"role\":\"User\"", "\"role\":\"Admin\""));
        var result = await ValidateAsync(string.Join('.', parts), Options);

        Assert.False(result.IsValid);
    }

    [Fact]
    public async Task Abgelaufenes_Access_Token_wird_abgelehnt()
    {
        // Token "vor einer Stunde" ausgestellt, also seit 45 Minuten abgelaufen.
        var oneHourAgo = new FakeTimeProvider(DateTimeOffset.UtcNow.AddHours(-1));
        var service = new TokenService(Options, oneHourAgo);

        var accessToken = service.CreateAccessToken(TestUser);
        var result = await ValidateAsync(accessToken.Token, Options);

        Assert.False(result.IsValid);
        Assert.IsType<SecurityTokenExpiredException>(result.Exception);
    }

    [Fact]
    public void CreateRefreshToken_ist_lang_und_jedes_Mal_anders()
    {
        var first = TokenService.CreateRefreshToken();
        var second = TokenService.CreateRefreshToken();

        // 64 Zufallsbytes ergeben 86 Zeichen Base64Url.
        Assert.Equal(86, first.Length);
        Assert.NotEqual(first, second);
    }

    [Fact]
    public void HashRefreshToken_ist_SHA256_und_immer_gleich_fuer_dasselbe_Token()
    {
        var token = TokenService.CreateRefreshToken();

        var hash = TokenService.HashRefreshToken(token);

        Assert.Equal(64, hash.Length);
        Assert.Equal(hash, TokenService.HashRefreshToken(token));
        Assert.NotEqual(token, hash);
    }
}
