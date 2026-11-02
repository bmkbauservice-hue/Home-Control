namespace HomeControl.Api.Blink;

// Einstellungen für den Blink-Zugang aus dem Konfigurationsabschnitt "Blink".
// Beide Werte gehören NICHT in appsettings.json oder Git, sondern lokal in die
// .NET User Secrets (siehe docs/BLINK.md).
public class BlinkOptions
{
    public const string SectionName = "Blink";

    // UUID aus dem Erst-Login (tools/BlinkSpike). Muss unverändert bleiben, sonst verlangt
    // Blink beim nächsten Login wieder eine neue PIN (siehe docs/BLINK.md).
    public string HardwareId { get; set; } = string.Empty;

    // Nur für den allerersten Start wichtig: Startwert für das Refresh-Token aus dem Spike.
    // Sobald die Datenbank ein Token hat, wird dieser Wert nicht mehr gelesen
    // (siehe BlinkCredentialStore) und kann danach aus den User Secrets entfernt werden.
    public string InitialRefreshToken { get; set; } = string.Empty;

    // Schalter: Darf DIESER Prozess Blink abfragen? Standard an, in appsettings.Development.json aus.
    // Grund: Dienst (Port 5200) und app-starten.sh (Port 5111) teilen sich homecontrol.db. Erneuern beide das
    // rotierende Blink-Token, macht der zweite das Token des ersten ungültig (siehe docs/BLINK.md,
    // "Ein Prozess pro Datenbank"). Beim Entwickeln einschalten mit Blink__Enabled=true.
    public bool Enabled { get; set; } = true;

    // Nur die HardwareId ist zwingend. Fehlt sie, ist Blink nicht eingerichtet,
    // unabhängig davon, ob schon ein Token in der Datenbank liegt.
    public List<string> GetMissingSettings()
    {
        var missing = new List<string>();
        if (string.IsNullOrWhiteSpace(HardwareId)) missing.Add("Blink:HardwareId");
        return missing;
    }
}
