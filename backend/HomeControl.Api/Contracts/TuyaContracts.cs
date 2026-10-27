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
