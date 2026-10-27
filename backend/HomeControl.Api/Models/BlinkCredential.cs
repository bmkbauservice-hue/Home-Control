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

    public DateTimeOffset UpdatedAtUtc { get; set; }
}
