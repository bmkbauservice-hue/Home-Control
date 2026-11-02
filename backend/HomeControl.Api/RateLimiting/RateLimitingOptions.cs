namespace HomeControl.Api.RateLimiting;

// Einstellungen für das Rate Limiting aus dem Konfigurationsabschnitt "RateLimiting"
// (appsettings.json). Kein Geheimnis, deshalb dürfen die Werte im Repo stehen.
//
// Beispiel: PermitLimit 10, WindowSeconds 60 heißt "höchstens 10 Anfragen pro Minute".
// Danach antwortet die API mit 429, bis das nächste Zeitfenster beginnt (siehe docs/RATE-LIMITING.md).
public class RateLimitingOptions
{
    public const string SectionName = "RateLimiting";

    // Anmelden, Registrieren, Token erneuern: pro Gerät (Client-IP) gezählt.
    public FixedWindowLimit Login { get; set; } = new() { PermitLimit = 10, WindowSeconds = 60 };

    // Alle Endpunkte, die echte Aufrufe bei der Tuya-Cloud auslösen: EIN gemeinsamer Zähler für alle,
    // weil das Tuya-Kontingent auch nur einmal da ist.
    public FixedWindowLimit Tuya { get; set; } = new() { PermitLimit = 20, WindowSeconds = 60 };

    // Blink-Vorschaubilder: pro Gerät (Client-IP). Das Backend hält Bilder ohnehin 60 Sekunden vor, die Grenze
    // schützt vor einer Flut von Anfragen (z. B. eine Schleife im Browser).
    public FixedWindowLimit BlinkThumbnails { get; set; } = new() { PermitLimit = 30, WindowSeconds = 60 };

    // Blink "Neues Bild": pro KAMERA höchstens 1 pro Minute. Jedes neue Bild weckt eine Akku-Kamera auf.
    public FixedWindowLimit BlinkSnapshot { get; set; } = new() { PermitLimit = 1, WindowSeconds = 60 };
}

// Ein festes Zeitfenster: höchstens PermitLimit Anfragen pro WindowSeconds Sekunden.
public class FixedWindowLimit
{
    public int PermitLimit { get; set; }

    public int WindowSeconds { get; set; }

    // 0 oder negativ ergibt keinen Sinn und würde erst bei der ersten Anfrage auffallen.
    public bool IsValid => PermitLimit > 0 && WindowSeconds > 0;
}
