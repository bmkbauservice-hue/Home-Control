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

// Ergebnis von "Neues Bild": Completed = Blink hat den Befehl als erledigt gemeldet (sonst nach 40 s aufgegeben).
// ThumbnailTakenAtUtc = Aufnahmezeit des Vorschaubilds danach (null, wenn Blink sie nicht nennt).
public record BlinkSnapshotResult(bool Completed, DateTimeOffset? ThumbnailTakenAtUtc);

// Ein Blink-System (bei Blink "network"). Armed = null: Blink nennt den Zustand nicht.
public record BlinkSystemInfo(string Id, string Name, bool? Armed);

// Ergebnis des Scharfschaltens. Armed ist der NACH dem Befehl neu gelesene Zustand.
public record BlinkArmResult(bool CommandCompleted, bool? Armed, bool AlreadyArmed);

// Ergebnis der Livebild-Probe: Sind echte Videodaten angekommen?
public record BlinkLiveProbeResult(bool VideoReceived, int VideoFrames, long VideoBytes, string? Problem);

// Ein Clip vom USB-Stick am Sync Module. Key ist der kurze Schlüssel fürs Abspielen.
public record BlinkClip(string Key, string CameraName, DateTimeOffset CreatedAtUtc);

public record BlinkClipVideo(byte[] Content);

// Problem: Warum die Liste leer oder unvollständig sein kann, sonst null.
public record BlinkClipList(IReadOnlyList<BlinkClip> Clips, string? Problem);

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

    // BEWUSSTE AUSNAHME von "nur lesen" (docs/BLINK.md): lässt die Kamera ein neues Vorschaubild aufnehmen.
    // Weckt Akku-Kameras auf und kostet Batterie, schaltet aber nichts (nicht scharf/unscharf, keine Aufnahme).
    // Nur für Admins, höchstens 1× pro Minute und Kamera (Endpunkt). null: Kamera unbekannt.
    Task<BlinkSnapshotResult?> RequestNewThumbnailAsync(string cameraId, CancellationToken cancellationToken);

    Task<IReadOnlyList<BlinkSystemInfo>> GetSystemsAsync(CancellationToken cancellationToken);

    // Schaltet ein System scharf und liest den Zustand danach neu. Ausdrücklicher Nutzerauftrag vom 08.11.2026,
    // nur über Regeln eines Admins. null: System unbekannt.
    Task<BlinkArmResult?> ArmSystemAsync(string systemId, CancellationToken cancellationToken);

    // Fordert kurz ein Livebild an und zählt echte Videodaten. Weckt die Kamera. null: Kamera unbekannt.
    Task<BlinkLiveProbeResult?> ProbeLiveViewAsync(string cameraId, CancellationToken cancellationToken);

    // Clips vom USB-Stick. Löscht nie etwas. refresh = false nutzt die kurz zwischengespeicherte Liste.
    Task<BlinkClipList> GetClipsAsync(bool refresh, CancellationToken cancellationToken);

    // Lädt einen Clip als MP4. null: Schlüssel unbekannt.
    Task<BlinkClipVideo?> GetClipVideoAsync(string key, CancellationToken cancellationToken);
}
