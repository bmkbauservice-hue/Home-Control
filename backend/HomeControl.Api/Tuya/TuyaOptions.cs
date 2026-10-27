namespace HomeControl.Api.Tuya;

// Einstellungen für die Tuya-Cloud aus dem Konfigurationsabschnitt "Tuya".
//
// BaseUrl steht in appsettings.json (kein Geheimnis).
// ClientId, ClientSecret und TestDeviceId gehören NICHT in appsettings.json oder Git,
// sondern lokal in die .NET User Secrets (siehe README).
public class TuyaOptions
{
    public const string SectionName = "Tuya";

    // Central Europe Data Center laut Tuya-Dokumentation
    public string BaseUrl { get; set; } = "https://openapi.tuyaeu.com";

    public string ClientId { get; set; } = string.Empty;

    public string ClientSecret { get; set; } = string.Empty;

    public string TestDeviceId { get; set; } = string.Empty;

    // Namen der Einstellungen, die noch fehlen. Die Werte selbst werden nie ausgegeben.
    public List<string> GetMissingSettings()
    {
        var missing = new List<string>();
        if (string.IsNullOrWhiteSpace(ClientId)) missing.Add("Tuya:ClientId");
        if (string.IsNullOrWhiteSpace(ClientSecret)) missing.Add("Tuya:ClientSecret");
        if (string.IsNullOrWhiteSpace(TestDeviceId)) missing.Add("Tuya:TestDeviceId");
        return missing;
    }
}
