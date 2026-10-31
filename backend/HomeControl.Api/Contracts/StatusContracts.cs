using HomeControl.Api.Status;

namespace HomeControl.Api.Contracts;

// Antwort von GET /api/status (Status-LEDs).
// Tuya: "online" (Cloud erreichbar), "offline" (nicht erreichbar) oder "notConfigured".
// Devices: jedes Gerät mit State "online", "offline" oder "unknown" (siehe Status/OnlineStates).
// Die Tuya-Geräte-Ids stehen bewusst nicht drin.
public record StatusResponse(
    DateTimeOffset CheckedAtUtc,
    string Tuya,
    int DevicesOnline,
    int DevicesOffline,
    int DevicesUnknown,
    List<DeviceOnlineState> Devices);
