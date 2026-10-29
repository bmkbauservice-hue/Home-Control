namespace HomeControl.Api.Models;

// Ein Refresh-Token eines Benutzers. Damit holt sich das Frontend ein neues Access Token,
// ohne dass der Benutzer sein Passwort erneut eingibt.
//
// Gespeichert wird NUR der Hash (SHA-256) des Tokens, nie das Token selbst. Grund: Wer die
// Datenbankdatei in die Hände bekommt (Backup, kopierte homecontrol.db), soll damit keine
// gültigen Tokens haben. Beim Prüfen wird das eingereichte Token ebenfalls gehasht und der
// Hash verglichen. Anders als bei Passwörtern reicht hier ein schneller Hash ohne Salt: Das
// Token ist eine lange Zufallszahl (64 Byte), die man nicht durch Ausprobieren erraten kann.
//
// Nicht zu verwechseln mit dem Blink-Refresh-Token (BlinkCredential): Das gehört zum
// Blink-Konto und muss verschlüsselt (nicht gehasht) gespeichert werden, weil wir es selbst
// wieder an Blink schicken. Unser eigenes Token müssen wir nie wieder im Klartext kennen.
public class RefreshToken
{
    public Guid Id { get; set; } = Guid.NewGuid();

    // SHA-256 des Tokens als Hex-Zeichenkette (64 Zeichen). Eindeutiger Index.
    public string TokenHash { get; set; } = string.Empty;

    public Guid UserId { get; set; }

    public User User { get; set; } = null!;

    public DateTimeOffset CreatedAtUtc { get; set; }

    public DateTimeOffset ExpiresAtUtc { get; set; }

    // Gesetzt, sobald das Token nicht mehr gilt: bei Rotation (durch ein neues ersetzt) oder
    // bei Logout. Das Token wird bewusst NICHT gelöscht, sondern nur markiert: Taucht ein
    // bereits widerrufenes Token noch einmal auf, wurde es vermutlich gestohlen, und der Server
    // kann darauf reagieren (alle Tokens des Benutzers widerrufen).
    public DateTimeOffset? RevokedAtUtc { get; set; }
}
