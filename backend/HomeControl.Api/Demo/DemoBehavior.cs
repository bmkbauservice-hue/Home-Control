namespace HomeControl.Api.Demo;

// Zustand eines Demo-Geräts zu einem Zeitpunkt, so wie ihn die Fake-Cloud meldet.
// PowerWatt/VoltageVolt nur bei Steckdosen, DoorOpen nur bei der Tür.
public record DemoState(bool? SwitchOn, double? PowerWatt, double? VoltageVolt, bool? DoorOpen, int? Battery);

// Wie sich die Demo-Geräte verhalten. Reine Funktion: gleiche Uhrzeit, gleiches Ergebnis. Kein Zufall,
// der sich nicht wiederholen lässt, deshalb gut zu testen. Die kleinen Schwankungen kommen aus einer
// Prüfsumme über Gerät und Minute (Jitter), nicht aus Random.
public static class DemoBehavior
{
    private static readonly TimeZoneInfo Berlin = TimeZoneInfo.FindSystemTimeZoneById("Europe/Berlin");

    public static DemoState StateAt(DemoDevice device, DateTimeOffset nowUtc)
    {
        var local = TimeZoneInfo.ConvertTime(nowUtc, Berlin);
        var minute = local.Hour * 60 + local.Minute;   // Minute des Tages, 0 bis 1439
        var weekday = local.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday);
        var jitter = Jitter(device.Key, nowUtc);

        switch (device.Kind)
        {
            case DemoKind.Door:
                // Alle 20 Minuten für 5 Minuten offen. Der Abgleich fragt die Tür alle 3 Minuten,
                // erwischt also jedes Öffnen.
                return new DemoState(null, null, null, minute % 20 < 5, device.Battery);

            case DemoKind.Temperature:
                return new DemoState(null, null, null, null, device.Battery);

            case DemoKind.Light:
                return new DemoState(LightOn(device.Key, minute, weekday), null, null, null, null);

            default:
                if (!device.Online)
                {
                    // Ausgesteckt: Tuya meldet den letzten Stand (aus, 0 W).
                    return new DemoState(false, 0, 0, null, null);
                }

                var (on, watt) = Socket(device.Key, minute, jitter);
                return new DemoState(on, Math.Round(watt, 1), Math.Round(230 + 2 * jitter, 1), null, null);
        }
    }

    // Lampen nach Tageszeit. Die Leselampe geht zusätzlich tagsüber ab und zu an, damit in einer
    // Vorführung am Vormittag auch etwas passiert.
    private static bool LightOn(string key, int minute, bool weekday) => key switch
    {
        "leselampe" => Between(minute, 18, 0, 23, 0) || (minute / 12) % 3 == 0,
        "deckenlicht" => Between(minute, 6, 30, 8, 30) || Between(minute, 17, 30, 21, 0),
        "nachttisch" => Between(minute, 6, 0, 6, 30) || Between(minute, 21, 30, 23, 30),
        "schreibtisch" => weekday && Between(minute, 8, 0, 17, 0),
        _ => false,
    };

    // Steckdosen: an/aus und Leistung in Watt.
    private static (bool On, double Watt) Socket(string key, int minute, double jitter) => key switch
    {
        // Abends an, sonst aus
        "fernseher" => Between(minute, 18, 30, 23, 0) ? (true, 90 + 6 * jitter) : (false, 0),
        // Alle 30 Minuten für 4 Minuten an, dann zieht sie viel Strom
        "kaffee" => minute % 30 < 4 ? (true, 1200 + 40 * jitter) : (false, 0),
        // Immer an, der Kompressor läuft 8 von 20 Minuten
        "kuehlschrank" => (true, minute % 20 < 8 ? 92 + 5 * jitter : 1.5),
        // Tagsüber an, Leistung schwankt deutlich
        "computer" => Between(minute, 8, 0, 18, 0) ? (true, 110 + 35 * jitter) : (false, 0),
        _ => (false, 0),
    };

    private static bool Between(int minute, int fromHour, int fromMinute, int toHour, int toMinute) =>
        minute >= fromHour * 60 + fromMinute && minute < toHour * 60 + toMinute;

    // Wert zwischen -1 und 1, gleich für dasselbe Gerät in derselben Minute. Eigene Prüfsumme (FNV-1a),
    // weil string.GetHashCode() bei jedem Programmstart anders ausfällt.
    public static double Jitter(string key, DateTimeOffset nowUtc)
    {
        var hash = 2166136261u;
        foreach (var c in $"{key}:{nowUtc.ToUnixTimeSeconds() / 60}")
        {
            hash = (hash ^ c) * 16777619u;
        }

        return hash % 2001 / 1000.0 - 1;
    }
}
