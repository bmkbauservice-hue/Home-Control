using HomeControl.Api.Blink;
using Microsoft.Data.Sqlite;

namespace HomeControl.Api.Demo;

// Demo-Modus: eine zweite Instanz mit Beispieldaten für Präsentationen (./demo-starten.sh, docs/DEMO.md).
// Sie läuft neben dem Dienst, mit eigener Datenbank, eigenem Port und ohne echte Geheimnisse.
//
// Sicherheitsregel: Der Demo-Modus gilt NUR zusammen mit der Umgebung "Demo" (appsettings.Demo.json), und die
// Umgebung "Demo" NUR mit dem Demo-Modus. Passt etwas nicht zusammen, startet die App gar nicht. So kann der
// Dienst (Production) nie versehentlich mit Fake-Daten laufen, und die Demo nie mit echten Zugangsdaten.
public static class DemoMode
{
    public const string EnvironmentName = "Demo";
    public const string Setting = "HomeControl:DemoMode";
    public const string PasswordSetting = "HomeControl:DemoPassword";

    public static bool IsEnabled(IConfiguration configuration) => configuration.GetValue<bool>(Setting);

    // Wirft eine verständliche Fehlermeldung, wenn Umgebung und Einstellungen nicht zusammenpassen.
    public static void EnsureConsistent(IHostEnvironment environment, IConfiguration configuration)
    {
        var enabled = IsEnabled(configuration);
        var demoEnvironment = environment.IsEnvironment(EnvironmentName);

        if (enabled && !demoEnvironment)
        {
            throw new InvalidOperationException(
                $"{Setting} ist nur in der Umgebung \"{EnvironmentName}\" erlaubt, nicht in \"{environment.EnvironmentName}\". " +
                "Die Demo startet man mit ./demo-starten.sh.");
        }

        if (!enabled)
        {
            if (demoEnvironment)
            {
                throw new InvalidOperationException(
                    $"Die Umgebung \"{EnvironmentName}\" verlangt {Setting}=true (appsettings.Demo.json).");
            }

            return;
        }

        // Keine echten Geheimnisse in der Demo: Die User Secrets enthalten Tuya-, Blink- und JWT-Schlüssel.
        if (configuration.GetValue<bool>("HomeControl:UseUserSecrets"))
        {
            throw new InvalidOperationException("Im Demo-Modus werden keine User Secrets geladen (HomeControl:UseUserSecrets).");
        }

        // Ein Prozess pro Datenbank: Die Demo darf nie in die echte homecontrol.db schreiben.
        var dataSource = new SqliteConnectionStringBuilder(configuration.GetConnectionString("DefaultConnection")).DataSource;
        if (!Path.GetFileName(dataSource).Contains("demo", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "Im Demo-Modus muss die Datenbank eine eigene Datei mit \"demo\" im Namen sein (z. B. homecontrol-demo.db).");
        }

        if (string.IsNullOrWhiteSpace(configuration[PasswordSetting]))
        {
            throw new InvalidOperationException(
                $"Im Demo-Modus fehlt das Passwort für {DemoHouse.Email} ({PasswordSetting}). Start mit ./demo-starten.sh.");
        }
    }

    // Dependency Injection je nach Einstellung: Im Demo-Modus bekommen dieselben Interfaces bzw. derselbe
    // TuyaClient einen Fake hinter sich. Endpunkte, Abgleich und Oberfläche bleiben unverändert.
    public static void AddTuyaAndBlink(IServiceCollection services, bool demoMode, IHttpClientBuilder tuyaHttpClient)
    {
        if (demoMode)
        {
            // Der echte TuyaClient bekommt statt des Netzwerks die Fake-Cloud als "letzten" Handler.
            services.AddSingleton<DemoSimulation>();
            tuyaHttpClient.ConfigurePrimaryHttpMessageHandler(provider =>
                new DemoTuyaHandler(provider.GetRequiredService<TimeProvider>(), provider.GetRequiredService<DemoSimulation>()));
            services.AddSingleton<IBlinkCameraClient, DemoBlinkCameraClient>();
            services.AddScoped<DemoDataSeeder>();
        }
        else
        {
            services.AddSingleton<IBlinkCameraClient, BlinkHttpCameraClient>();
        }
    }
}
