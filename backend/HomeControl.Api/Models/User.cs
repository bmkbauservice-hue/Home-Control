namespace HomeControl.Api.Models;

// Ein Benutzer von Home Control (Anmeldung mit E-Mail und Passwort).
// Nicht verwechseln mit den Zugangsdaten für Tuya oder Blink: Die gehören zu externen Diensten,
// dieser Benutzer meldet sich an UNSERER API an.
public class User
{
    public Guid Id { get; set; } = Guid.NewGuid();

    // Immer klein geschrieben und ohne Leerzeichen am Rand gespeichert (siehe AuthService),
    // damit "Mario@Example.de" und "mario@example.de" derselbe Benutzer sind. Eindeutiger Index.
    public string Email { get; set; } = string.Empty;

    // Nie das Passwort selbst, nur der Hash (PasswordHasher von ASP.NET Core: PBKDF2 mit Salt).
    public string PasswordHash { get; set; } = string.Empty;

    // "Admin" oder "User", siehe Roles.
    public string Role { get; set; } = Roles.User;

    public DateTimeOffset CreatedAtUtc { get; set; }

    public List<RefreshToken> RefreshTokens { get; set; } = [];
}

// Die beiden Rollen als Konstanten, damit sich kein Tippfehler ("admin", "Admn") einschleicht.
public static class Roles
{
    public const string Admin = "Admin";
    public const string User = "User";
}
