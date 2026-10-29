using HomeControl.Api.Auth;
using HomeControl.Api.Data;
using HomeControl.Api.Models;
using HomeControl.Api.Tests.Fakes;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace HomeControl.Api.Tests;

// Tests für AuthService mit einer eigenen SQLite-Datenbank im Arbeitsspeicher (die echte
// homecontrol.db bleibt unberührt): Registrieren, Login, Refresh-Token-Rotation, Ablauf,
// Widerruf und Reuse Detection.
public class AuthServiceTests : IDisposable
{
    private const string AdminEmail = "admin@example.de";
    private const string AdminPassword = "Admin-Passwort-123";

    private readonly SqliteConnection connection;
    private readonly ApplicationDbContext db;
    private readonly FakeTimeProvider time = new();
    private readonly JwtOptions options = new()
    {
        SigningKey = "test-schluessel-nur-fuer-unit-tests-mindestens-32-zeichen",
        AccessTokenMinutes = 15,
        RefreshTokenDays = 30,
    };
    private readonly AuthService service;

    public AuthServiceTests()
    {
        connection = new SqliteConnection("DataSource=:memory:");
        connection.Open();

        var dbOptions = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(connection)
            .Options;
        db = new ApplicationDbContext(dbOptions);
        db.Database.EnsureCreated();

        service = new AuthService(
            db,
            new PasswordHasher<User>(),
            new TokenService(options, time),
            options,
            time,
            NullLogger<AuthService>.Instance);
    }

    public void Dispose()
    {
        db.Dispose();
        connection.Dispose();
    }

    private Task<RegisterResult> RegisterFirstAdminAsync() =>
        service.RegisterAsync(AdminEmail, AdminPassword, null, callerIsAdmin: false, CancellationToken.None);

    private async Task<AuthTokens> LoginAdminAsync()
    {
        await RegisterFirstAdminAsync();
        var tokens = await service.LoginAsync(AdminEmail, AdminPassword, CancellationToken.None);
        Assert.NotNull(tokens);
        return tokens;
    }

    // --- Registrieren ---

    [Fact]
    public async Task Register_erster_Benutzer_wird_Admin_und_Passwort_ist_gehasht()
    {
        var result = await service.RegisterAsync("  Admin@Example.DE ", AdminPassword, null, callerIsAdmin: false, CancellationToken.None);

        Assert.Equal(RegisterStatus.Created, result.Status);
        var user = await db.Users.SingleAsync();
        Assert.Equal(AdminEmail, user.Email);
        Assert.Equal(Roles.Admin, user.Role);
        Assert.NotEqual(AdminPassword, user.PasswordHash);
        Assert.DoesNotContain(AdminPassword, user.PasswordHash);
    }

    [Fact]
    public async Task Register_zweiter_Benutzer_ohne_Admin_wird_abgelehnt()
    {
        await RegisterFirstAdminAsync();

        var result = await service.RegisterAsync("gast@example.de", "Gast-Passwort-123", null, callerIsAdmin: false, CancellationToken.None);

        Assert.Equal(RegisterStatus.AdminRequired, result.Status);
        Assert.Equal(1, await db.Users.CountAsync());
    }

    [Fact]
    public async Task Register_durch_Admin_legt_normalen_User_an()
    {
        await RegisterFirstAdminAsync();

        var result = await service.RegisterAsync("jessie@example.de", "Jessie-Passwort-123", null, callerIsAdmin: true, CancellationToken.None);

        Assert.Equal(RegisterStatus.Created, result.Status);
        Assert.Equal(Roles.User, result.User!.Role);
    }

    [Fact]
    public async Task Register_gleiche_Email_in_anderer_Schreibweise_wird_abgelehnt()
    {
        await RegisterFirstAdminAsync();

        var result = await service.RegisterAsync("ADMIN@example.de", "Anderes-Passwort-123", null, callerIsAdmin: true, CancellationToken.None);

        Assert.Equal(RegisterStatus.EmailTaken, result.Status);
    }

    // --- Login ---

    [Fact]
    public async Task Login_erfolgreich_liefert_Tokens_und_speichert_nur_den_Hash()
    {
        var tokens = await LoginAdminAsync();

        Assert.False(string.IsNullOrEmpty(tokens.AccessToken.Token));
        Assert.Equal(Roles.Admin, tokens.User.Role);
        Assert.Equal(time.GetUtcNow().AddDays(30), tokens.RefreshTokenExpiresAtUtc);

        var stored = await db.RefreshTokens.SingleAsync();
        Assert.NotEqual(tokens.RefreshToken, stored.TokenHash);
        Assert.Equal(TokenService.HashRefreshToken(tokens.RefreshToken), stored.TokenHash);
        Assert.Null(stored.RevokedAtUtc);
    }

    [Fact]
    public async Task Login_mit_falschem_Passwort_liefert_null()
    {
        await RegisterFirstAdminAsync();

        var tokens = await service.LoginAsync(AdminEmail, "falsches-Passwort", CancellationToken.None);

        Assert.Null(tokens);
        Assert.Equal(0, await db.RefreshTokens.CountAsync());
    }

    [Fact]
    public async Task Login_mit_unbekannter_Email_liefert_null()
    {
        await RegisterFirstAdminAsync();

        var tokens = await service.LoginAsync("unbekannt@example.de", AdminPassword, CancellationToken.None);

        Assert.Null(tokens);
    }

    // --- Refresh ---

    [Fact]
    public async Task Refresh_rotiert_das_Token()
    {
        var login = await LoginAdminAsync();
        time.Advance(TimeSpan.FromMinutes(20));

        var refreshed = await service.RefreshAsync(login.RefreshToken, CancellationToken.None);

        Assert.NotNull(refreshed);
        Assert.NotEqual(login.RefreshToken, refreshed.RefreshToken);

        // Altes Token widerrufen, neues aktiv.
        var oldHash = TokenService.HashRefreshToken(login.RefreshToken);
        var newHash = TokenService.HashRefreshToken(refreshed.RefreshToken);
        Assert.NotNull((await db.RefreshTokens.SingleAsync(t => t.TokenHash == oldHash)).RevokedAtUtc);
        Assert.Null((await db.RefreshTokens.SingleAsync(t => t.TokenHash == newHash)).RevokedAtUtc);
    }

    [Fact]
    public async Task Refresh_mit_abgelaufenem_Token_wird_abgelehnt()
    {
        var login = await LoginAdminAsync();
        time.Advance(TimeSpan.FromDays(31));

        var refreshed = await service.RefreshAsync(login.RefreshToken, CancellationToken.None);

        Assert.Null(refreshed);
    }

    [Fact]
    public async Task Refresh_nach_Logout_wird_abgelehnt()
    {
        var login = await LoginAdminAsync();
        await service.LogoutAsync(login.RefreshToken, CancellationToken.None);

        var refreshed = await service.RefreshAsync(login.RefreshToken, CancellationToken.None);

        Assert.Null(refreshed);
    }

    [Fact]
    public async Task Refresh_mit_unbekanntem_Token_wird_abgelehnt()
    {
        await LoginAdminAsync();

        var refreshed = await service.RefreshAsync(TokenService.CreateRefreshToken(), CancellationToken.None);

        Assert.Null(refreshed);
    }

    [Fact]
    public async Task Altes_Token_nach_Rotation_beendet_alle_Sitzungen()
    {
        // Reuse Detection: Jemand hat das alte Token kopiert und benutzt es nach der Rotation.
        var login = await LoginAdminAsync();
        var refreshed = await service.RefreshAsync(login.RefreshToken, CancellationToken.None);
        Assert.NotNull(refreshed);

        var reused = await service.RefreshAsync(login.RefreshToken, CancellationToken.None);

        Assert.Null(reused);
        // Auch das neue, eigentlich gültige Token ist jetzt widerrufen.
        Assert.Null(await service.RefreshAsync(refreshed.RefreshToken, CancellationToken.None));
        Assert.All(await db.RefreshTokens.ToListAsync(), t => Assert.NotNull(t.RevokedAtUtc));
    }
}
