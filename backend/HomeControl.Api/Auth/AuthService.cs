using HomeControl.Api.Data;
using HomeControl.Api.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace HomeControl.Api.Auth;

// Die eigentliche Logik der Anmeldung: Registrieren, Einloggen, Token erneuern, Abmelden.
// Die Endpunkte (Endpoints/AuthEndpoints.cs) kümmern sich nur um HTTP: Eingaben prüfen,
// Cookie setzen, Statuscode wählen. So lässt sich diese Klasse ohne HTTP testen.
//
// Benutzer, Passwörter und Rollen verwaltet ASP.NET Core Identity (UserManager, RoleManager).
// Hier steht nur, was Home Control selbst dazu beiträgt: wer der erste Admin wird, und die
// Refresh-Tokens (JWT-Teil), die Identity nicht kennt.
public class AuthService(
    ApplicationDbContext db,
    UserManager<User> userManager,
    RoleManager<IdentityRole<Guid>> roleManager,
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
        var isFirstUser = !await userManager.Users.AnyAsync(cancellationToken);
        if (!isFirstUser && !callerIsAdmin)
        {
            return new RegisterResult(RegisterStatus.AdminRequired, null, []);
        }

        var normalizedEmail = NormalizeEmail(email);
        var user = new User
        {
            Id = Guid.NewGuid(),
            // Die E-Mail-Adresse ist zugleich der Benutzername.
            UserName = normalizedEmail,
            Email = normalizedEmail,
            CreatedAtUtc = timeProvider.GetUtcNow(),
        };

        // Identity prüft die Passwortregeln, erzeugt Salt und Hash (PBKDF2), speichert den Benutzer
        // und achtet auf doppelte E-Mail-Adressen. Fehler kommen als IdentityResult zurück.
        var created = await userManager.CreateAsync(user, password);
        if (!created.Succeeded)
        {
            var emailTaken = created.Errors.Any(e => e.Code is "DuplicateUserName" or "DuplicateEmail");
            return new RegisterResult(
                emailTaken ? RegisterStatus.EmailTaken : RegisterStatus.Invalid,
                null,
                created.Errors.ToList());
        }

        // Erster Benutzer: immer Admin. Sonst: was der Admin gewählt hat, Standard "User".
        var role = isFirstUser || requestedRole == Roles.Admin ? Roles.Admin : Roles.User;
        if (!await roleManager.RoleExistsAsync(role))
        {
            await roleManager.CreateAsync(new IdentityRole<Guid>(role));
        }

        var roleAdded = await userManager.AddToRoleAsync(user, role);
        if (!roleAdded.Succeeded)
        {
            // Kein Benutzer ohne Rolle zurücklassen.
            await userManager.DeleteAsync(user);
            throw new InvalidOperationException(
                "Rolle konnte nicht zugewiesen werden: " + string.Join("; ", roleAdded.Errors.Select(e => e.Description)));
        }

        return new RegisterResult(RegisterStatus.Created, new AuthUser(user.Id, normalizedEmail, role), []);
    }

    // Prüft E-Mail und Passwort. Bei unbekannter E-Mail oder falschem Passwort gibt es bewusst
    // dieselbe Antwort, damit niemand herausfinden kann, welche E-Mail-Adressen es gibt.
    // Nach zu vielen Fehlversuchen sperrt Identity das Konto für kurze Zeit (Lockout).
    public async Task<LoginResult> LoginAsync(string email, string password, CancellationToken cancellationToken)
    {
        var user = await userManager.FindByEmailAsync(NormalizeEmail(email));
        if (user is null)
        {
            return new LoginResult(LoginStatus.InvalidCredentials, null);
        }

        if (await userManager.IsLockedOutAsync(user))
        {
            return new LoginResult(LoginStatus.LockedOut, null);
        }

        // Das eingegebene Passwort wird mit demselben Salt gehasht und mit dem gespeicherten
        // Hash verglichen. Zurückrechnen aus dem Hash geht nicht. Stammt der Hash von einem
        // älteren Verfahren, hasht Identity bei dieser Gelegenheit selbst neu.
        if (!await userManager.CheckPasswordAsync(user, password))
        {
            await userManager.AccessFailedAsync(user);
            return await userManager.IsLockedOutAsync(user)
                ? new LoginResult(LoginStatus.LockedOut, null)
                : new LoginResult(LoginStatus.InvalidCredentials, null);
        }

        await userManager.ResetAccessFailedCountAsync(user);
        return new LoginResult(LoginStatus.Success, await IssueTokensAsync(user, cancellationToken));
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

    public async Task<AuthUser?> FindUserAsync(Guid userId, CancellationToken cancellationToken)
    {
        var user = await userManager.FindByIdAsync(userId.ToString());
        return user is null ? null : new AuthUser(user.Id, user.Email!, await GetRoleAsync(user));
    }

    // Identity kennt pro Benutzer eine Liste von Rollen. Home Control benutzt genau eine:
    // Admin, sonst User.
    private async Task<string> GetRoleAsync(User user)
    {
        var roles = await userManager.GetRolesAsync(user);
        return roles.Contains(Roles.Admin) ? Roles.Admin : Roles.User;
    }

    // Neues Access Token und neues Refresh-Token. Vom Refresh-Token wird nur der Hash gespeichert,
    // das Token selbst geht einmal an den Client (im Cookie) und dann nie wieder.
    private async Task<AuthTokens> IssueTokensAsync(User user, CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        var authUser = new AuthUser(user.Id, user.Email!, await GetRoleAsync(user));
        var accessToken = tokenService.CreateAccessToken(authUser);
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

        return new AuthTokens(accessToken, refreshToken, refreshTokenExpiresAt, authUser);
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
    // Identity hat Eingabe oder Passwort abgelehnt (z. B. Passwort zu kurz). Siehe Errors.
    Invalid,
}

public record RegisterResult(RegisterStatus Status, AuthUser? User, IReadOnlyList<IdentityError> Errors);

public enum LoginStatus
{
    Success,
    InvalidCredentials,
    // Zu viele Fehlversuche, das Konto ist kurz gesperrt.
    LockedOut,
}

public record LoginResult(LoginStatus Status, AuthTokens? Tokens);

// Alles, was nach Login oder Refresh an den Client geht. RefreshToken landet im Cookie,
// der Rest im JSON der Antwort.
public record AuthTokens(
    AccessToken AccessToken,
    string RefreshToken,
    DateTimeOffset RefreshTokenExpiresAtUtc,
    AuthUser User);
