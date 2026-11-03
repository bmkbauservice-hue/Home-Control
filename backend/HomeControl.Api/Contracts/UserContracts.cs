using System.ComponentModel.DataAnnotations;

namespace HomeControl.Api.Contracts;

// Eine Zeile in GET /api/users (Einstellungsseite, nur Admin). LockedOut: nach 5 Fehlversuchen kurz gesperrt.
public record UserListResponse(Guid Id, string Email, string Role, DateTimeOffset CreatedAtUtc, bool LockedOut);

// PUT /api/users/{id}/role
public record ChangeRoleRequest(
    [Required(ErrorMessage = "Bitte eine Rolle angeben.")]
    [RegularExpression("^(Admin|User)$", ErrorMessage = "Rolle muss \"Admin\" oder \"User\" sein.")]
    string Role);

// POST /api/auth/password – eigenes Passwort ändern. Die Regeln für das neue Passwort prüft Identity.
public record ChangePasswordRequest(
    [Required(ErrorMessage = "Bitte das bisherige Passwort eingeben.")]
    [StringLength(128, ErrorMessage = "Das Passwort darf höchstens 128 Zeichen lang sein.")]
    string CurrentPassword,
    [Required(ErrorMessage = "Bitte ein neues Passwort eingeben.")]
    [StringLength(128, ErrorMessage = "Das Passwort darf höchstens 128 Zeichen lang sein.")]
    string NewPassword);

// GET /api/system/info (nur Admin): Was läuft hier? Für die Einstellungsseite, Bereich "Info".
// DatabaseFile nur der Dateiname, nie der ganze Pfad (der enthielte den Benutzernamen des Rechners).
public record SystemInfoResponse(
    string Version,
    string? Commit,
    string Environment,
    bool DemoMode,
    string DotNetVersion,
    DateTimeOffset StartedAtUtc,
    string DatabaseFile,
    long? DatabaseBytes,
    int TuyaDefaultSyncSeconds,
    int Users,
    int Rooms,
    int Devices,
    int Events);
