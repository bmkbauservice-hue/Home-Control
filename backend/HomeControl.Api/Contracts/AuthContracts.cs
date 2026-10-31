using System.ComponentModel.DataAnnotations;

namespace HomeControl.Api.Contracts;

// Die Regeln stehen als DataAnnotations direkt an den Feldern. builder.Services.AddValidation()
// (Program.cs, .NET 10) prüft sie, BEVOR der Endpunkt läuft. Ist etwas ungültig, antwortet die API
// mit 400 und ProblemDetails (errors je Feld), der Endpunkt wird gar nicht erst aufgerufen.
// Die Passwortregeln (mind. 8 Zeichen, Ziffer, Groß- und Kleinbuchstabe) prüft Identity beim
// Anlegen (IdentityRegistration.cs), hier steht nur die Obergrenze.

// Role ist optional und wird nur beachtet, wenn ein Admin einen Benutzer anlegt
// ("Admin" oder "User", Standard "User"). Der allererste Benutzer wird immer Admin.
public record RegisterRequest(
    [Required(ErrorMessage = "Bitte eine E-Mail-Adresse eingeben.")]
    [EmailAddress(ErrorMessage = "Bitte eine gültige E-Mail-Adresse eingeben.")]
    [StringLength(256, ErrorMessage = "Die E-Mail-Adresse darf höchstens 256 Zeichen lang sein.")]
    string Email,
    [Required(ErrorMessage = "Bitte ein Passwort eingeben.")]
    // Obergrenze, damit niemand den Server mit riesigen Passwörtern beschäftigt (Hashing ist absichtlich langsam).
    [StringLength(128, ErrorMessage = "Das Passwort darf höchstens 128 Zeichen lang sein.")]
    string Password,
    [RegularExpression("^(Admin|User)$", ErrorMessage = "Rolle muss \"Admin\" oder \"User\" sein.")]
    string? Role);

// Beim Login bewusst KEINE Passwortregeln (nur Pflicht und Obergrenze): Sonst verrät die Antwort
// etwas über die Regeln statt nur "E-Mail oder Passwort ist falsch".
public record LoginRequest(
    [Required(ErrorMessage = "Bitte eine E-Mail-Adresse eingeben.")]
    [EmailAddress(ErrorMessage = "Bitte eine gültige E-Mail-Adresse eingeben.")]
    [StringLength(256, ErrorMessage = "Die E-Mail-Adresse darf höchstens 256 Zeichen lang sein.")]
    string Email,
    [Required(ErrorMessage = "Bitte ein Passwort eingeben.")]
    [StringLength(128, ErrorMessage = "Das Passwort darf höchstens 128 Zeichen lang sein.")]
    string Password);

// Was die API über einen Benutzer herausgibt. Nie den Passwort-Hash.
public record UserResponse(Guid Id, string Email, string Role);

// Antwort auf Login und Refresh. Das Refresh-Token steht bewusst NICHT im JSON, sondern nur im
// HTTP-only Cookie: JavaScript im Browser kommt so nicht daran (Schutz bei XSS).
public record LoginResponse(string AccessToken, DateTimeOffset AccessTokenExpiresAtUtc, UserResponse User);
