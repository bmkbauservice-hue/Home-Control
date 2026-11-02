using Microsoft.Extensions.Configuration;

namespace HomeControl.Api.Tests;

// Beim Entwickeln laufen Dienst (5200) und app-starten.sh (5111) oft gleichzeitig auf derselben Datenbank.
// Damit sie sich nicht in die Quere kommen, ist in appsettings.Development.json alles aus, was im Hintergrund
// mit fremden Diensten spricht. Dieser Test prüft die Datei selbst (die Testfabrik setzt eigene Werte).
public class DevelopmentSettingsTests
{
    private static IConfiguration LoadDevelopmentSettings()
    {
        // Vom Ausgabeordner der Tests nach oben suchen, bis backend/HomeControl.Api gefunden ist.
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "HomeControl.Api", "appsettings.Development.json")))
        {
            dir = dir.Parent;
        }
        Assert.NotNull(dir);

        return new ConfigurationBuilder()
            .AddJsonFile(Path.Combine(dir.FullName, "HomeControl.Api", "appsettings.Development.json"))
            .Build();
    }

    [Fact]
    public void Tuya_Hintergrund_Abgleich_ist_beim_Entwickeln_aus()
    {
        Assert.Equal(0, LoadDevelopmentSettings().GetValue<int>("Tuya:AutoSyncSeconds", 900));
    }

    [Fact]
    public void Blink_ist_beim_Entwickeln_aus()
    {
        Assert.False(LoadDevelopmentSettings().GetValue("Blink:Enabled", true));
    }
}
