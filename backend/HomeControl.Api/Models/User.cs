using Microsoft.AspNetCore.Identity;

namespace HomeControl.Api.Models;

// Ein Benutzer von Home Control (Anmeldung mit E-Mail und Passwort).
// Nicht verwechseln mit den Zugangsdaten für Tuya oder Blink: Die gehören zu externen Diensten,
// dieser Benutzer meldet sich an UNSERER API an.
//
// IdentityUser<Guid> (ASP.NET Core Identity) bringt schon mit: Id, UserName, Email, PasswordHash,
// SecurityStamp, Sperre nach Fehlversuchen (Lockout) und mehr. Diese Felder werden hier bewusst
// NICHT noch einmal definiert, nur das, was Home Control zusätzlich braucht.
// Die Rolle (Admin/User) steht nicht mehr im Benutzer, sondern in den Identity-Rollentabellen
// (UserManager.AddToRoleAsync).
public class User : IdentityUser<Guid>
{
    public DateTimeOffset CreatedAtUtc { get; set; }

    public List<RefreshToken> RefreshTokens { get; set; } = [];
}

// Die beiden Rollen als Konstanten, damit sich kein Tippfehler ("admin", "Admn") einschleicht.
public static class Roles
{
    public const string Admin = "Admin";
    public const string User = "User";
}
