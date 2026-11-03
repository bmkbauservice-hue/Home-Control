namespace HomeControl.Api.Contracts;

// Antwort für GET /api/demo: Läuft diese Instanz im Demo-Modus? DemoEmail nur im Demo-Modus (für die
// Anmeldeseite vorausgefüllt), sonst null. Das Passwort steht nie in einer Antwort, nur im Terminal.
public record DemoInfoResponse(bool DemoMode, string? DemoEmail);
