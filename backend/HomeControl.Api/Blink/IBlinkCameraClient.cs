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

    // In diesem Prozess ausgeschaltet (Blink:Enabled = false, Standard beim Entwickeln). Es wird nichts abgefragt.
    Disabled,
}

// Blink ist in diesem Prozess ausgeschaltet (Blink:Enabled = false).
public class BlinkDisabledException() : Exception("Blink ist in dieser Umgebung ausgeschaltet (Blink:Enabled).");

public record BlinkStatusResult(BlinkConnectionState State, DateTimeOffset? LastSuccessfulFetchUtc);

// Eine Kamera, wie wir sie nach außen zeigen. Battery/Temperature/Online stammen aus
// unbenannten Feldern der Blink-Antwort (siehe BlinkHttpCameraClient) und sind deshalb
// "best effort": Sie können fehlen, bis die echten Feldnamen per Spike geprüft wurden.
// HasThumbnail/ThumbnailTakenAtUtc: Gibt es ein letztes Vorschaubild, und wann wurde es aufgenommen
// (null, wenn Blink den Zeitpunkt nicht mitliefert)? Die Bild-Adresse selbst bleibt im Backend.
public record BlinkCameraInfo(
    string Id,
    string Name,
    string? Battery,
    double? TemperatureCelsius,
    bool? Online,
    bool HasThumbnail = false,
    DateTimeOffset? ThumbnailTakenAtUtc = null);

// Das letzte vorhandene Vorschaubild einer Kamera.
public record BlinkThumbnail(byte[] Content, string ContentType, DateTimeOffset? TakenAtUtc);

// Blink ist nicht eingerichtet: weder Datenbank noch Bootstrap-Wert haben ein Token.
public class BlinkNotConfiguredException(string detail) : Exception(detail);

// Blink hat die Anmeldung abgelehnt oder ein vorhandenes Token war ungültig.
// Es wird bewusst nicht automatisch neu versucht (Sperrgefahr bei einer inoffiziellen API).
public class BlinkLoginRequiredException(string detail) : Exception(detail);

// Blink hat die Anfrage anders abgelehnt (z. B. unerwartete Antwort, Netzwerkfehler).
public class BlinkApiException(string detail) : Exception(detail);

// Unsere schmale Abstraktion über Blink. Nur lesend, nur das, was die Oberfläche braucht.
// Dahinter steckt BlinkHttpCameraClient (eigener HttpClient, Homescreen v3); eine spätere Implementierung
// gegen einen blinkpy-Sidecar müsste nur dieses Interface erfüllen.
public interface IBlinkCameraClient
{
    Task<BlinkStatusResult> GetStatusAsync(CancellationToken cancellationToken);

    Task<IReadOnlyList<BlinkCameraInfo>> GetCamerasAsync(CancellationToken cancellationToken);

    // Letztes VORHANDENES Vorschaubild (löst nie ein neues Foto aus). null: Kamera unbekannt oder kein Bild.
    Task<BlinkThumbnail?> GetThumbnailAsync(string cameraId, CancellationToken cancellationToken);
}
