namespace HomeControl.Api.Contracts;

// Daten, die das Frontend beim Anlegen oder Bearbeiten eines Geräts schickt.
// IsActive und LastValue fehlen bewusst: Diese Werte liefert später die Geräte-Anbindung.
public record DeviceRequest(string Name, string Type, Guid RoomId);

// Daten, die die API zurückgibt. LastValue bleibt null, solange es keine echten Messwerte gibt.
public record DeviceResponse(Guid Id, string Name, string Type, Guid RoomId, string RoomName, string? LastValue);
