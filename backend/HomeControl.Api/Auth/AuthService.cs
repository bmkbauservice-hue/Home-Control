using HomeControl.Api.Data;
using HomeControl.Api.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace HomeControl.Api.Auth;

// Die eigentliche Logik der Anmeldung: Registrieren, Einloggen, Token erneuern, Abmelden.
// Die Endpunkte (Endpoints/AuthEndpoints.cs) kümmern sich nur um HTTP: Eingaben prüfen,
// Cookie setzen, Statuscode wählen. So lässt sich diese Klasse ohne HTTP testen.
public class AuthService(
    ApplicationDbContext db,
    IPasswordHasher<User> passwordHasher,
    TokenService tokenService,
    JwtOptions options,
    TimeProvider timeProvider,
    ILogger<AuthService> logger)
{
    // "  Mario@Example.DE " und "mario@example.de" sollen derselbe Benutzer sein.
    public static string NormalizeEmail(string email) => email.Trim().ToLowerInvariant();

    // Legt einen Benutzer an.
    // - Gibt es noch keinen Benutzer, darf sich jeder registrieren und wird Admin.
    //   So kommt man beim ersten Start überhaupt an ein Admin-Konto.
    // - Danach darf nur noch ein Admin neue Benutzer anlegen (callerIsAdmin).
    public async Task<RegisterResult> RegisterAsync(
        string email,
        string password,
        string? requestedRole,
        bool callerIsAdmin,
        CancellationToken cancellationToken)
    {
        var isFirstUser = !await db.Users.AnyAsync(cancellationToken);
        if (!isFirstUser && !callerIsAdmin)
        {
            return new RegisterResult(RegisterStatus.AdminRequired, null);
        }

        var normalizedEmail = NormalizeEmail(email);
        if (await db.Users.AnyAsync(u => u.Email == normalizedEmail, cancellationToken))
        {
            return new RegisterResult(RegisterStatus.EmailTaken, null);
        }

        var user = new User
        {
            Email = normalizedEmail,
            // Erster Benutzer: immer Admin. Sonst: was der Admin gewählt hat, Standard "User".
            Role = isFirstUser || requestedRole == Roles.Admin ? Roles.Admin : Roles.User,
            CreatedAtUtc = timeProvider.GetUtcNow(),
        };

        // PasswordHasher von ASP.NET Core: PBKDF2 mit zufälligem Salt und vielen Durchläufen.
        // Das Ergebnis enthält Verfahren, Salt und Hash in einem Text. Das Passwort selbst
        // wird nirgends gespeichert.
        user.PasswordHash = passwordHasher.HashPassword(user, password);

        db.Users.Add(user);
        await db.SaveChangesAsync(cancellationToken);

        return new RegisterResult(RegisterStatus.Created, user);
    }

    // Prüft E-Mail und Passwort. Liefert null, wenn eins von beiden nicht stimmt. Bewusst ohne
    // Unterschied, damit niemand herausfinden kann, welche E-Mail-Adressen es gibt.
    public async Task<AuthTokens?> LoginAsync(string email, string password, CancellationToken cancellationToken)
    {
        var normalizedEmail = NormalizeEmail(email);
        var user = await db.Users.FirstOrDefaultAsync(u => u.Email == normalizedEmail, cancellationToken);
        if (user is null)
        {
            return null;
        }

        // Das eingegebene Passwort wird mit demselben Salt gehasht und mit dem gespeicherten
        // Hash verglichen. Zurückrechnen aus dem Hash geht nicht.
        var result = passwordHasher.VerifyHashedPassword(user, user.PasswordHash, password);
        if (result == PasswordVerificationResult.Failed)
        {
            return null;
        }

        // Passwort stimmt, aber der Hash stammt von einem älteren Verfahren (z. B. weniger
        // Durchläufe): bei dieser Gelegenheit mit dem aktuellen Verfahren neu hashen.
        if (result == PasswordVerificationResult.SuccessRehashNeeded)
        {
            user.PasswordHash = passwordHasher.HashPassword(user, password);
        }

        return await IssueTokensAsync(user, cancellationToken);
    }

    // Refresh-Token-Rotation: Das alte Refresh-Token wird widerrufen und durch ein neues ersetzt.
    // Liefert null, wenn das Token unbekannt, abgelaufen oder widerrufen ist.
    public async Task<AuthTokens?> RefreshAsync(string? refreshToken, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(refreshToken))
        {
            return null;
        }

        var tokenHash = TokenService.HashRefreshToken(refreshToken);
        var stored = await db.RefreshTokens
            .Include(t => t.User)
            .FirstOrDefaultAsync(t => t.TokenHash == tokenHash, cancellationToken);

        if (stored is null)
        {
            return null;
        }

        var now = timeProvider.GetUtcNow();

        // Ein schon widerrufenes Token wird noch einmal benutzt. Das passiert normalerweise nie:
        // Nach einer Rotation hat das Frontend ja das neue Token. Wahrscheinlich hat jemand das
        // alte Token kopiert. Wir wissen nicht, wer der echte Benutzer ist, also beenden wir
        // vorsichtshalber ALLE Sitzungen dieses Benutzers ("Reuse Detection").
        if (stored.RevokedAtUtc is not null)
        {
            logger.LogWarning(
                "Widerrufenes Refresh-Token erneut benutzt. Alle Sitzungen von Benutzer {UserId} werden beendet.",
                stored.UserId);
            await RevokeAllAsync(stored.UserId, now, cancellationToken);
            return null;
        }

        if (stored.ExpiresAtUtc <= now)
        {
            return null;
        }

        // Rotation: altes Token widerrufen (nicht löschen, siehe Models/RefreshToken.cs),
        // neues ausstellen. Beides wird in IssueTokensAsync gemeinsam gespeichert.
        stored.RevokedAtUtc = now;
        return await IssueTokensAsync(stored.User, cancellationToken);
    }

    // Abmelden: Das Refresh-Token wird widerrufen. Das Access Token kann der Server nicht
    // zurückholen, es läuft aber nach spätestens 15 Minuten ab, und das Frontend löscht es.
    public async Task LogoutAsync(string? refreshToken, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(refreshToken))
        {
            return;
        }

        var tokenHash = TokenService.HashRefreshToken(refreshToken);
        var stored = await db.RefreshTokens.FirstOrDefaultAsync(t => t.TokenHash == tokenHash, cancellationToken);
        if (stored is not null && stored.RevokedAtUtc is null)
        {
            stored.RevokedAtUtc = timeProvider.GetUtcNow();
            await db.SaveChangesAsync(cancellationToken);
        }
    }

    public Task<User?> FindUserAsync(Guid userId, CancellationToken cancellationToken) =>
        db.Users.FirstOrDefaultAsync(u => u.Id == userId, cancellationToken);

    // Neues Access Token und neues Refresh-Token. Vom Refresh-Token wird nur der Hash gespeichert,
    // das Token selbst geht einmal an den Client (im Cookie) und dann nie wieder.
    private async Task<AuthTokens> IssueTokensAsync(User user, CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        var accessToken = tokenService.CreateAccessToken(user);
        var refreshToken = TokenService.CreateRefreshToken();
        var refreshTokenExpiresAt = now.AddDays(options.RefreshTokenDays);

        db.RefreshTokens.Add(new RefreshToken
        {
            TokenHash = TokenService.HashRefreshToken(refreshToken),
            UserId = user.Id,
            CreatedAtUtc = now,
            ExpiresAtUtc = refreshTokenExpiresAt,
        });
        await db.SaveChangesAsync(cancellationToken);

        return new AuthTokens(accessToken, refreshToken, refreshTokenExpiresAt, user);
    }

    private async Task RevokeAllAsync(Guid userId, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var activeTokens = await db.RefreshTokens
            .Where(t => t.UserId == userId && t.RevokedAtUtc == null)
            .ToListAsync(cancellationToken);

        foreach (var token in activeTokens)
        {
            token.RevokedAtUtc = now;
        }

        await db.SaveChangesAsync(cancellationToken);
    }
}

public enum RegisterStatus
{
    Created,
    EmailTaken,
    AdminRequired,
}

public record RegisterResult(RegisterStatus Status, User? User);

// Alles, was nach Login oder Refresh an den Client geht. RefreshToken landet im Cookie,
// der Rest im JSON der Antwort.
public record AuthTokens(
    AccessToken AccessToken,
    string RefreshToken,
    DateTimeOffset RefreshTokenExpiresAtUtc,
    User User);
