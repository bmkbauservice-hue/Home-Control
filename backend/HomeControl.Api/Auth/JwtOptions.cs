namespace HomeControl.Api.Auth;

// Einstellungen für die Anmeldung aus dem Konfigurationsabschnitt "Jwt".
//
// Issuer, Audience und die Laufzeiten stehen in appsettings.json (kein Geheimnis).
// Der SigningKey ist geheim: Wer ihn kennt, kann selbst gültige Tokens ausstellen, zum Beispiel
// eines mit der Rolle "Admin". Er gehört deshalb in die .NET User Secrets, nie in Git
// (siehe jwt-schluessel-einrichten.sh und docs/AUTHENTIFIZIERUNG.md).
public class JwtOptions
{
    public const string SectionName = "Jwt";

    // HMAC-SHA256 braucht einen Schlüssel mit mindestens 256 Bit, also 32 Byte.
    public const int MinimumSigningKeyLength = 32;

    // Wer das Token ausgestellt hat und für wen es gedacht ist. Beides prüft der Server beim
    // Lesen eines Tokens mit, damit kein Token einer anderen Anwendung durchgeht.
    public string Issuer { get; set; } = "HomeControl";

    public string Audience { get; set; } = "HomeControl";

    public string SigningKey { get; set; } = string.Empty;

    // Access Token: kurz gültig. Wird es gestohlen, ist der Schaden zeitlich begrenzt.
    public int AccessTokenMinutes { get; set; } = 15;

    // Refresh-Token: länger gültig, liegt aber nur im HTTP-only Cookie und in der Datenbank
    // (als Hash) und lässt sich jederzeit widerrufen.
    public int RefreshTokenDays { get; set; } = 30;
}
