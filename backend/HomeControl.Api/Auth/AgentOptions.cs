namespace HomeControl.Api.Auth;

// Einstellungen für den KI-Agenten (n8n) aus dem Konfigurationsabschnitt "Agent".
//
// Der Agent meldet sich nicht mit E-Mail und Passwort an, sondern schickt bei jedem Aufruf den
// Header "X-Api-Key". Der Schlüssel ist geheim und gehört in die .NET User Secrets, nie in Git
// (siehe agent-schluessel-einrichten.sh). Ohne Schlüssel ist der Zugang für den Agenten aus.
public class AgentOptions
{
    public const string SectionName = "Agent";

    // Kurze Schlüssel lassen sich erraten. 32 Zeichen reichen bei zufälligem Text locker.
    public const int MinimumKeyLength = 32;

    public string ApiKey { get; set; } = string.Empty;
}
