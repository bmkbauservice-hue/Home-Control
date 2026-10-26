namespace HomeControl.Api.Contracts;

// Daten, die das Frontend beim Erfassen eines Ereignisses schickt.
// Den Zeitpunkt setzt bewusst der Server, damit alle Einträge dieselbe Uhr (UTC) nutzen.
public record DeviceEventRequest(string Action, string? Value);

// Daten, die die API für ein Ereignis zurückgibt. Timestamp ist immer UTC.
public record DeviceEventResponse(Guid Id, DateTimeOffset Timestamp, string Action, string? Value);
