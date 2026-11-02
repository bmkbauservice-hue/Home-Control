namespace HomeControl.Api.Contracts;

// Antwort für GET /api/blink/status.
public record BlinkStatusResponse(string State, DateTimeOffset? LastSuccessfulFetchUtc, string Note);

// Antwort für GET /api/blink/cameras: eine Zeile je Kamera.
// Battery, TemperatureCelsius und Online können fehlen (siehe docs/BLINK.md, bekannte Grenzen).
// HasThumbnail: Es gibt ein letztes Vorschaubild (abrufbar unter /api/blink/cameras/{id}/thumbnail, nur mit
// Anmeldung). ThumbnailTakenAtUtc: wann es aufgenommen wurde, null wenn Blink es nicht mitliefert.
public record BlinkCameraResponse(
    string Id,
    string Name,
    string? Battery,
    double? TemperatureCelsius,
    bool? Online,
    bool HasThumbnail,
    DateTimeOffset? ThumbnailTakenAtUtc);
