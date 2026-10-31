namespace HomeControl.Api.Status;

// Die drei Zustände einer Status-LED in der Oberfläche.
public static class OnlineStates
{
    // grün: Tuya meldet das Gerät als verbunden bzw. die Tuya-Cloud ist erreichbar
    public const string Online = "online";

    // rot: Tuya meldet das Gerät als nicht verbunden bzw. die Tuya-Cloud ist nicht erreichbar
    public const string Offline = "offline";

    // grau: Wir wissen es nicht (Gerät nicht mit Tuya verknüpft, Tuya nicht eingerichtet oder
    // nicht erreichbar, Gerät in der Tuya-Cloud nicht mehr vorhanden)
    public const string Unknown = "unknown";

    // nur für die Tuya-Cloud selbst: Zugangsdaten fehlen in den User Secrets
    public const string NotConfigured = "notConfigured";
}

// So viel brauchen wir von einem Gerät aus unserer Datenbank für die Auswertung.
public record DeviceLink(Guid Id, string Name, string? TuyaDeviceId);

// Ergebnis pro Gerät: Id, Name und einer der Zustände aus OnlineStates.
public record DeviceOnlineState(Guid Id, string Name, string State);

// Entscheidet für jedes Gerät, welche Farbe seine LED bekommt.
// Bewusst eine reine Funktion ohne Datenbank und ohne HTTP: So lässt sie sich einfach testen.
public static class DeviceOnlineEvaluator
{
    // cloudOnline: Tuya-Geräte-Id → online laut Tuya (true, false oder null = Tuya sagt nichts).
    // null für die ganze Liste heißt: Tuya ist nicht eingerichtet oder gerade nicht erreichbar.
    public static List<DeviceOnlineState> Evaluate(
        IEnumerable<DeviceLink> devices,
        IReadOnlyDictionary<string, bool?>? cloudOnline) =>
        devices
            .Select(d => new DeviceOnlineState(d.Id, d.Name, StateOf(d.TuyaDeviceId, cloudOnline)))
            .ToList();

    private static string StateOf(string? tuyaDeviceId, IReadOnlyDictionary<string, bool?>? cloudOnline)
    {
        // Ohne Verknüpfung oder ohne Daten aus der Cloud können wir nichts sagen.
        if (tuyaDeviceId is null || cloudOnline is null)
        {
            return OnlineStates.Unknown;
        }

        // Verknüpft, aber in der Tuya-Cloud nicht (mehr) vorhanden, z. B. aus der App gelöscht.
        if (!cloudOnline.TryGetValue(tuyaDeviceId, out var online))
        {
            return OnlineStates.Unknown;
        }

        return online switch
        {
            true => OnlineStates.Online,
            false => OnlineStates.Offline,
            null => OnlineStates.Unknown,
        };
    }
}
