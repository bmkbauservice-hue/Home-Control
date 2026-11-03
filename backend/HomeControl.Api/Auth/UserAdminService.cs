using HomeControl.Api.Data;
using HomeControl.Api.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace HomeControl.Api.Auth;

// Ergebnis einer Änderung an einem Benutzer (Rolle ändern, löschen).
public enum UserChangeStatus
{
    Done,
    NotFound,
    // Es muss immer mindestens ein Admin übrig bleiben, sonst kann niemand mehr etwas verwalten.
    LastAdmin,
    // Sich selbst löschen oder herabstufen geht nicht. Das macht ein anderer Admin (Schutz vor Aussperren).
    Self,
}

public enum PasswordChangeStatus
{
    Done,
    NotFound,
    WrongPassword,
    // Identity hat das neue Passwort abgelehnt (zu kurz, keine Ziffer …), siehe Errors.
    Invalid,
}

public record UserListItem(Guid Id, string Email, string Role, DateTimeOffset CreatedAtUtc, bool LockedOut);

public record PasswordChangeResult(PasswordChangeStatus Status, IReadOnlyList<IdentityError> Errors);

// Benutzer verwalten (Einstellungsseite, nur Admin) und eigenes Passwort ändern.
// Anlegen läuft weiter über AuthService.RegisterAsync (POST /api/auth/register).
//
// Nach einer Änderung von Rolle oder Passwort werden Refresh-Tokens gelöscht (siehe unten): Das JWT kann der Server
// nicht zurückholen, es enthält die alte Rolle aber höchstens noch 15 Minuten (Jwt:AccessTokenMinutes).
// Jede Änderung steht im Log, mit Ids, ohne Passwörter (REVIEW.md N-9).
public class UserAdminService(
    UserManager<User> userManager,
    RoleManager<IdentityRole<Guid>> roleManager,
    ApplicationDbContext db,
    TimeProvider timeProvider,
    ILogger<UserAdminService> logger)
{
    // Ein Rollenwechsel oder Löschen gleichzeitig: Sonst könnten zwei Admins sich fast gleichzeitig
    // gegenseitig herabstufen, beide sähen "es gibt noch einen anderen Admin", und am Ende gäbe es keinen.
    private static readonly SemaphoreSlim AdminLock = new(1, 1);

    public async Task<List<UserListItem>> ListAsync(CancellationToken cancellationToken)
    {
        var users = await userManager.Users.OrderBy(u => u.Email).ToListAsync(cancellationToken);
        var admins = (await userManager.GetUsersInRoleAsync(Roles.Admin)).Select(u => u.Id).ToHashSet();
        var now = timeProvider.GetUtcNow();

        return users
            .Select(u => new UserListItem(u.Id, u.Email!, admins.Contains(u.Id) ? Roles.Admin : Roles.User,
                u.CreatedAtUtc, u.LockoutEnd > now))
            .ToList();
    }

    public async Task<UserChangeStatus> ChangeRoleAsync(Guid userId, string role, Guid callerId, CancellationToken cancellationToken)
    {
        if (userId == callerId)
        {
            return UserChangeStatus.Self;
        }

        await AdminLock.WaitAsync(cancellationToken);
        try
        {
            var user = await userManager.FindByIdAsync(userId.ToString());
            if (user is null)
            {
                return UserChangeStatus.NotFound;
            }

            var isAdmin = await userManager.IsInRoleAsync(user, Roles.Admin);
            if (isAdmin == (role == Roles.Admin))
            {
                return UserChangeStatus.Done;   // schon so, nichts zu tun
            }

            if (isAdmin && await CountAdminsAsync() <= 1)
            {
                return UserChangeStatus.LastAdmin;
            }

            if (!await roleManager.RoleExistsAsync(role))
            {
                await roleManager.CreateAsync(new IdentityRole<Guid>(role));
            }

            Check(await userManager.RemoveFromRoleAsync(user, isAdmin ? Roles.Admin : Roles.User));
            Check(await userManager.AddToRoleAsync(user, role));
            await RevokeRefreshTokensAsync(user.Id, keepTokenHash: null, cancellationToken);

            logger.LogInformation("Benutzerverwaltung: Admin {CallerId} hat die Rolle von {UserId} auf {Role} geändert.",
                callerId, user.Id, role);
            return UserChangeStatus.Done;
        }
        finally
        {
            AdminLock.Release();
        }
    }

    public async Task<UserChangeStatus> DeleteAsync(Guid userId, Guid callerId, CancellationToken cancellationToken)
    {
        if (userId == callerId)
        {
            return UserChangeStatus.Self;
        }

        await AdminLock.WaitAsync(cancellationToken);
        try
        {
            var user = await userManager.FindByIdAsync(userId.ToString());
            if (user is null)
            {
                return UserChangeStatus.NotFound;
            }

            if (await userManager.IsInRoleAsync(user, Roles.Admin) && await CountAdminsAsync() <= 1)
            {
                return UserChangeStatus.LastAdmin;
            }

            // Refresh-Tokens werden mitgelöscht (Cascade im ApplicationDbContext).
            Check(await userManager.DeleteAsync(user));
            logger.LogInformation("Benutzerverwaltung: Admin {CallerId} hat Benutzer {UserId} gelöscht.", callerId, userId);
            return UserChangeStatus.Done;
        }
        finally
        {
            AdminLock.Release();
        }
    }

    // Eigenes Passwort ändern. Danach werden alle ANDEREN Sitzungen beendet (z. B. ein vergessenes Tablet),
    // die aktuelle bleibt angemeldet (ihr Refresh-Token kommt aus dem Cookie).
    public async Task<PasswordChangeResult> ChangePasswordAsync(
        Guid userId,
        string currentPassword,
        string newPassword,
        string? currentRefreshToken,
        CancellationToken cancellationToken)
    {
        var user = await userManager.FindByIdAsync(userId.ToString());
        if (user is null)
        {
            return new PasswordChangeResult(PasswordChangeStatus.NotFound, []);
        }

        // Identity prüft das alte Passwort und die Regeln für das neue, hasht es und speichert.
        var result = await userManager.ChangePasswordAsync(user, currentPassword, newPassword);
        if (!result.Succeeded)
        {
            var wrong = result.Errors.Any(e => e.Code == nameof(IdentityErrorDescriber.PasswordMismatch));
            return new PasswordChangeResult(wrong ? PasswordChangeStatus.WrongPassword : PasswordChangeStatus.Invalid,
                result.Errors.ToList());
        }

        var keep = string.IsNullOrEmpty(currentRefreshToken) ? null : TokenService.HashRefreshToken(currentRefreshToken);
        await RevokeRefreshTokensAsync(user.Id, keep, cancellationToken);
        logger.LogInformation("Benutzer {UserId} hat sein Passwort geändert, andere Sitzungen beendet.", user.Id);
        return new PasswordChangeResult(PasswordChangeStatus.Done, []);
    }

    private async Task<int> CountAdminsAsync() => (await userManager.GetUsersInRoleAsync(Roles.Admin)).Count;

    // Beendet die Sitzungen eines Benutzers (außer keepTokenHash) durch LÖSCHEN der Refresh-Tokens, nicht durch
    // Widerrufen. Ein widerrufenes Token, das das andere Gerät noch einmal schickt, hielte AuthService für gestohlen
    // (Reuse Detection) und beendete dann ALLE Sitzungen, auch die gerade benutzte. Ein gelöschtes Token ist nur
    // unbekannt: Das andere Gerät wird abgemeldet, sonst passiert nichts.
    private async Task RevokeRefreshTokensAsync(Guid userId, string? keepTokenHash, CancellationToken cancellationToken)
    {
        var tokens = await db.RefreshTokens
            .Where(t => t.UserId == userId && t.TokenHash != keepTokenHash)
            .ToListAsync(cancellationToken);
        db.RefreshTokens.RemoveRange(tokens);
        await db.SaveChangesAsync(cancellationToken);
    }

    private static void Check(IdentityResult result)
    {
        if (!result.Succeeded)
        {
            throw new InvalidOperationException(string.Join("; ", result.Errors.Select(e => e.Description)));
        }
    }
}
