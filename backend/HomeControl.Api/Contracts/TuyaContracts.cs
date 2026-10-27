using HomeControl.Api.Tuya;

namespace HomeControl.Api.Contracts;

// Antwort für den Status des konfigurierten Tuya-Testgeräts.
// RetrievedAtUtc ist der Zeitpunkt, zu dem unser Server die Werte bei Tuya abgerufen hat.
// Status enthält Codes und Werte unverändert aus der Tuya-Cloud.
// Die Geräte-Id wird bewusst nicht zurückgegeben.
public record TuyaDeviceStatusResponse(
    DateTimeOffset RetrievedAtUtc,
    List<TuyaStatusItem> Status,
    string Note);

// Antwort von POST /api/tuya/test-device/sync: derselbe Status wie oben und dazu,
// was in unserer Datenbank passiert ist (siehe TuyaSyncResults).
public record TuyaDeviceSyncResponse(
    DateTimeOffset RetrievedAtUtc,
    List<TuyaStatusItem> Status,
    string Note,
    string SyncResult);
