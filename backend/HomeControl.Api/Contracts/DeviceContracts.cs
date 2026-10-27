namespace HomeControl.Api.Contracts;

// Daten, die das Frontend beim Anlegen oder Bearbeiten eines Geräts schickt.
// IsActive und LastValue fehlen bewusst: Diese Werte liefert die Geräte-Anbindung.
// TuyaDeviceId ist optional: leer oder null = nicht mit einem Tuya-Gerät verknüpft.
public record DeviceRequest(string Name, string Type, Guid RoomId, string? TuyaDeviceId = null);

// Daten, die die API zurückgibt. LastValue bleibt null, solange es keine echten Messwerte gibt.
public record DeviceResponse(
    Guid Id, string Name, string Type, Guid RoomId, string RoomName, string? LastValue, string? TuyaDeviceId);
