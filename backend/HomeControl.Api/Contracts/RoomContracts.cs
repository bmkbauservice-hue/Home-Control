namespace HomeControl.Api.Contracts;

// Daten, die das Frontend beim Anlegen oder Bearbeiten schickt.
public record RoomRequest(string Name);

// Daten, die die API an das Frontend zurückgibt.
// Wir geben bewusst nicht die Datenbank-Klasse "Room" direkt zurück,
// damit das Frontend nur sieht, was es wirklich braucht.
public record RoomResponse(Guid Id, string Name, int DeviceCount);
