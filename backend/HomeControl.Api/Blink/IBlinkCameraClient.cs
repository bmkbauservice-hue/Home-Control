namespace HomeControl.Api.Blink;

// Ob und wie wir gerade mit Blink verbunden sind.
public enum BlinkConnectionState
{
    // Kein Token vorhanden (weder in der Datenbank noch als Bootstrap-Wert in den User Secrets).
    NotConfigured,

    // Letzter Login/Abruf ist fehlgeschlagen. Es wird NICHT automatisch erneut versucht,
    // siehe docs/BLINK.md. Erst ein neuer Lauf von tools/BlinkSpike behebt das.
    LoginRequired,

    // Letzter Abruf (oder der zwischengespeicherte Stand) war erfolgreich.
    Connected,
}

public record BlinkStatusResult(BlinkConnectionState State, DateTimeOffset? LastSuccessfulFetchUtc);

// Eine Kamera, wie wir sie nach außen zeigen. Battery/Temperature/Online stammen aus
// unbenannten Feldern der Blink-Antwort (siehe BlinkNetCameraClient) und sind deshalb
// "best effort": Sie können fehlen, bis die echten Feldnamen per Spike geprüft wurden.
public record BlinkCameraInfo(
    string Id,
    string Name,
    string? Battery,
    double? TemperatureCelsius,
    bool? Online);

// Blink ist nicht eingerichtet: weder Datenbank noch Bootstrap-Wert haben ein Token.
public class BlinkNotConfiguredException(string detail) : Exception(detail);

// Blink hat die Anmeldung abgelehnt oder ein vorhandenes Token war ungültig.
// Es wird bewusst nicht automatisch neu versucht (Sperrgefahr bei einer inoffiziellen API).
public class BlinkLoginRequiredException(string detail) : Exception(detail);

// Blink hat die Anfrage anders abgelehnt (z. B. unerwartete Antwort, Netzwerkfehler).
public class BlinkApiException(string detail) : Exception(detail);

// Unsere schmale Abstraktion über Blink. Nur lesend, nur das, was die Oberfläche braucht.
// Dahinter steckt aktuell Blink.NET (BlinkNetCameraClient); eine spätere Implementierung
// gegen einen blinkpy-Sidecar müsste nur dieses Interface erfüllen.
public interface IBlinkCameraClient
{
    Task<BlinkStatusResult> GetStatusAsync(CancellationToken cancellationToken);

    Task<IReadOnlyList<BlinkCameraInfo>> GetCamerasAsync(CancellationToken cancellationToken);
}
