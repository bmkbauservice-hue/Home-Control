namespace HomeControl.Api.Models;

// Genau eine Zeile (Id ist immer 1): die Blink-Anmeldedaten. Das Refresh-Token liegt nur
// verschlüsselt hier, siehe HomeControl.Api.Blink.BlinkCredentialStore.
public class BlinkCredential
{
    public int Id { get; set; } = 1;

    // UUID aus dem Erst-Login. Nur zur Nachvollziehbarkeit hier gespeichert, maßgeblich
    // bleibt der Wert aus den User Secrets (Blink:HardwareId).
    public string HardwareId { get; set; } = string.Empty;

    // Verschlüsselt über die ASP.NET Data Protection API (nie im Klartext in der Datenbank).
    public string EncryptedRefreshToken { get; set; } = string.Empty;

    // Hash (SHA-256) des zuletzt aus den User Secrets übernommenen Blink:InitialRefreshToken.
    // Dient nur dem Vergleich: Weicht der aktuelle Secrets-Wert davon ab, ist er neu (z. B.
    // nach einem erneuten Lauf von tools/BlinkSpike) und wird ohne Neustart übernommen.
    // Kein Geheimnis, aber auch kein Klartext-Token – ein Hash reicht für den Vergleich.
    public string? InitialRefreshTokenHash { get; set; }

    public DateTimeOffset UpdatedAtUtc { get; set; }
}
