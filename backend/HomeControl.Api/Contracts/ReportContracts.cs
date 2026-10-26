namespace HomeControl.Api.Contracts;

// Tagesauswertung für einen UTC-Tag (00:00 UTC einschließlich bis 00:00 UTC des Folgetags ausschließlich).
// CurrentDeviceCount ist der aktuelle Gerätebestand, nicht der Bestand am ausgewählten Tag.
// "Aktive Geräte" fehlen bewusst: IsActive wird noch nicht durch Ereignisse aktualisiert.
public record DailyReportResponse(
    DateOnly Date,
    int CurrentDeviceCount,
    int EventCount,
    List<ActionCount> Actions,
    List<DeviceEventCount> TopDevices);

// Wie oft eine Aktion an diesem Tag vorkam.
public record ActionCount(string Action, int Count);

// Ein Eintrag in der Rangliste der Geräte mit den meisten Ereignissen.
public record DeviceEventCount(Guid DeviceId, string DeviceName, int EventCount);
