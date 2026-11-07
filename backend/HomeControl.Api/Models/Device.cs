namespace HomeControl.Api.Models;

public class Device
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string Name { get; set; } = string.Empty;

    public string Type { get; set; } = string.Empty;

    public bool IsActive { get; set; }

    public string? LastValue { get; set; }

    // Id des Geräts in der Tuya-Cloud, wenn es mit einem echten Gerät verknüpft ist.
    // Kein Geheimnis (ohne Client-Secret nutzlos), aber nur in der lokalen Datenbank, nicht in Git.
    // Jedes Tuya-Gerät kann höchstens mit einem Gerät verknüpft sein (eindeutiger Index).
    public string? TuyaDeviceId { get; set; }

    // Letzte Messwerte von Tuya (null = Gerät liefert diesen Wert nicht oder noch nie abgefragt).
    // Nur der letzte Messwert. Für den Verbrauch wird separat eine laufende Schätzung gespeichert.
    public double? PowerWatt { get; set; }

    // Eigener Zeitpunkt, damit ein Spannungs- oder Akkumesswert die Wattprobe nicht verjüngt.
    public DateTimeOffset? PowerMeasuredAtUtc { get; set; }

    public double? EstimatedEnergyKwh { get; set; }

    public double? VoltageVolt { get; set; }

    public int? BatteryPercent { get; set; }

    // Wann die Messwerte oben zuletzt von einem ONLINE-Gerät kamen. Bei Offline-Geräten liefert
    // Tuya nur den alten Stand, dann bleiben Werte und Zeitpunkt unverändert.
    public DateTimeOffset? MeasuredAtUtc { get; set; }

    // Wie oft der Tuya-Hintergrund-Abgleich dieses Gerät abfragt, in Sekunden.
    // null = Standard (Tuya:AutoSyncSeconds, 30 Minuten). Kürzer z. B. für die Tür (180 = 3 Minuten),
    // damit ein Öffnen nicht verpasst wird. Jeder Abruf kostet Tuya-Kontingent (docs/TUYA.md).
    public int? TuyaSyncSeconds { get; set; }

    public Guid RoomId { get; set; }

    public Room Room { get; set; } = null!;

    public List<DeviceEvent> Events { get; set; } = [];
}

