namespace HomeControl.Api.Contracts;

// Daten, die das Frontend beim Anlegen oder Bearbeiten eines Geräts schickt.
// IsActive und LastValue fehlen bewusst: Diese Werte liefert die Geräte-Anbindung.
// TuyaDeviceId ist optional: leer oder null = nicht mit einem Tuya-Gerät verknüpft.
// TuyaSyncSeconds: eigener Abgleich-Takt in Sekunden (null = Standard), siehe DeviceEndpoints.AllowedTuyaSyncSeconds.
public record DeviceRequest(string Name, string Type, Guid RoomId, string? TuyaDeviceId = null, int? TuyaSyncSeconds = null,
    string? GoogleHomeDeviceId = null);

// Daten, die die API zurückgibt. LastValue bleibt null, solange es keine echten Messwerte gibt.
// PowerWatt, VoltageVolt, BatteryPercent und MeasuredAtUtc kommen von Tuya (null = kein Wert).
public record DeviceResponse(
    Guid Id,
    string Name,
    string Type,
    Guid RoomId,
    string RoomName,
    string? LastValue,
    string? TuyaDeviceId,
    double? PowerWatt,
    double? VoltageVolt,
    int? BatteryPercent,
    DateTimeOffset? MeasuredAtUtc,
    int? TuyaSyncSeconds,
    DateTimeOffset? PowerMeasuredAtUtc,
    double? EstimatedEnergyKwh,
    string? GoogleHomeDeviceId,
    DateTimeOffset? BatteryReportedAtUtc);

