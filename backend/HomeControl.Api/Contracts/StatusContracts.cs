using HomeControl.Api.Status;

namespace HomeControl.Api.Contracts;

// Antwort von GET /api/status (Status-LEDs).
// Tuya: "online" (Cloud erreichbar), "offline" (nicht erreichbar), "unknown" (Hintergrund-Abgleich hat
// noch nicht abgefragt) oder "notConfigured".
// Devices: jedes Gerät mit State "online", "offline" oder "unknown" (siehe Status/OnlineStates).
// TuyaCheckedAtUtc: wann die Online-Liste zuletzt erfolgreich von Tuya kam (null = noch nie).
// Die Tuya-Geräte-Ids stehen bewusst nicht drin.
public record StatusResponse(
    DateTimeOffset CheckedAtUtc,
    string Tuya,
    int DevicesOnline,
    int DevicesOffline,
    int DevicesUnknown,
    List<DeviceOnlineState> Devices,
    DateTimeOffset? TuyaCheckedAtUtc);
