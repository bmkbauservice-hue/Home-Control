using System.Diagnostics;
using System.Text.Json;
using Blink;
using Microsoft.Extensions.Configuration;

// Spike zum Erst-Login bei Blink mit 2FA. Läuft bewusst NICHT im Backend: Hier kommen
// E-Mail/Passwort kurzzeitig vor, und nur ein Mensch kann die PIN eingeben.
// Ergebnis: HardwareId + Refresh-Token landen direkt (nie ausgegeben) in den User Secrets
// des Backends, damit HomeControl.Api ab dem nächsten Start darüber einloggen kann.

var config = new ConfigurationBuilder()
    .AddUserSecrets<Program>()
    .Build();

const string BackendProjectPath = "../../backend/HomeControl.Api";

// Bei einem erneuten Lauf (z. B. nach einem abgelaufenen Token) zuerst mit dem zuletzt
// gespeicherten Refresh-Token versuchen – dann ist keine neue PIN nötig.
var savedRefreshToken = config["Blink:RefreshToken"];
var savedHardwareId = config["Blink:HardwareId"];

var client = new BlinkClient
{
    // Laut Blink.NET-Doku braucht es eine kleine Pause zwischen Anfragen, sonst kommen
    // manchmal leere Antworten.
    GeneralSleepTime = 5000,
};

if (!string.IsNullOrWhiteSpace(savedHardwareId))
{
    // Dieselbe HardwareId wie beim letzten Mal verwenden: verbessert laut Blink.NET-Doku
    // die Stabilität des Refresh-Tokens.
    client.HardwareId = savedHardwareId;
}

Console.WriteLine($"HardwareId für diesen Lauf: {Mask(client.HardwareId)}");

var loggedIn = false;

if (!string.IsNullOrWhiteSpace(savedRefreshToken))
{
    Console.WriteLine("Vorhandenes Refresh-Token gefunden, versuche Login ohne PIN …");
    try
    {
        loggedIn = await client.TryLoginWithRefreshTokenAsync(savedRefreshToken);
    }
    catch (Exception ex)
    {
        Console.WriteLine($"Login mit Refresh-Token fehlgeschlagen: {ex.Message}");
    }

    if (!loggedIn)
    {
        Console.WriteLine("Refresh-Token wurde abgelehnt, es folgt ein normaler Login mit 2FA.");
    }
}

if (!loggedIn)
{
    var email = config["Blink:Email"];
    if (string.IsNullOrWhiteSpace(email))
    {
        Console.WriteLine("Fehlende E-Mail. Bitte vorher setzen (wird nirgends ausgegeben):");
        Console.WriteLine("  dotnet user-secrets set \"Blink:Email\" \"<deine-email>\" --project tools/BlinkSpike");
        return 1;
    }

    // Das Passwort wird NIE gespeichert (weder in User Secrets noch sonst irgendwo) –
    // jedes Mal verdeckt neu abfragen.
    Console.Write("Blink-Passwort eingeben (wird nirgends gespeichert): ");
    var password = ReadMaskedLine();
    if (string.IsNullOrWhiteSpace(password))
    {
        Console.WriteLine("Abgebrochen.");
        return 1;
    }

    Console.WriteLine("Login mit E-Mail/Passwort läuft …");
    bool loginAccepted;
    try
    {
        loginAccepted = await client.TryLoginAsync(email, password);
    }
    catch (Exception ex)
    {
        Console.WriteLine($"Login fehlgeschlagen: {ex.Message}");
        return 1;
    }

    if (!loginAccepted)
    {
        Console.WriteLine("Login fehlgeschlagen: Zugangsdaten wurden von Blink abgelehnt.");
        return 1;
    }

    Console.WriteLine("E-Mail/Passwort akzeptiert. Blink verlangt jetzt eine PIN (per SMS oder E-Mail).");

    while (!loggedIn)
    {
        Console.Write("PIN eingeben (leer lassen zum Abbrechen): ");
        var pin = Console.ReadLine();
        if (string.IsNullOrWhiteSpace(pin))
        {
            Console.WriteLine("Abgebrochen.");
            return 1;
        }

        try
        {
            loggedIn = await client.TryVerifyPinAsync(pin.Trim());
            if (!loggedIn)
            {
                Console.WriteLine("PIN war falsch oder abgelaufen, bitte erneut versuchen.");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"PIN-Prüfung fehlgeschlagen: {ex.Message}");
        }
    }
}

var refreshToken = client.RefreshToken;
if (string.IsNullOrWhiteSpace(refreshToken))
{
    Console.WriteLine("Unerwartet: kein Refresh-Token nach erfolgreicher Anmeldung erhalten.");
    return 1;
}

Console.WriteLine("Anmeldung erfolgreich.");

// Werte nie ausgeben: direkt in die User Secrets schreiben (Backend + dieses Tool selbst).
await SetUserSecretAsync(BackendProjectPath, "Blink:HardwareId", client.HardwareId);
await SetUserSecretAsync(BackendProjectPath, "Blink:InitialRefreshToken", refreshToken);
await SetUserSecretAsync(".", "Blink:HardwareId", client.HardwareId);
await SetUserSecretAsync(".", "Blink:RefreshToken", refreshToken);

Console.WriteLine("HardwareId und Refresh-Token wurden übernommen in:");
Console.WriteLine($"  Backend ({BackendProjectPath}): Blink:HardwareId, Blink:InitialRefreshToken");
Console.WriteLine("  Dieses Tool (tools/BlinkSpike): Blink:HardwareId, Blink:RefreshToken (für einen künftigen Lauf ohne PIN)");
Console.WriteLine();
Console.WriteLine("Sobald das Backend einmal erfolgreich gestartet ist und das Token in die Datenbank");
Console.WriteLine("übernommen hat, kann Blink:InitialRefreshToken aus den Backend-User-Secrets wieder");
Console.WriteLine("entfernt werden (siehe docs/BLINK.md, Abschnitt \"Bootstrap-Token entfernen\").");
Console.WriteLine();

Console.WriteLine("Netzwerke und Kameras:");
try
{
    var networks = await client.GetNetworksAsync();
    foreach (var (networkIdText, summary) in networks.Summary)
    {
        Console.WriteLine($"- Netzwerk \"{summary.Name}\" (onboarded: {summary.IsOnboarded})");

        if (!int.TryParse(networkIdText, out var networkId))
        {
            continue;
        }

        var raw = await client.GetCamerasAsync(networkId);
        PrintCameras(raw);
    }
}
catch (Exception ex)
{
    Console.WriteLine($"Netzwerke/Kameras konnten nicht abgerufen werden: {ex.Message}");
}

return 0;

static string Mask(string value) => value.Length <= 4 ? "····" : $"····{value[^4..]}";

// Liest eine Zeile verdeckt ein (zeigt "*" statt der eingegebenen Zeichen), ohne die Eingabe
// zwischenzuspeichern. Backspace löscht das zuletzt eingegebene Zeichen.
static string ReadMaskedLine()
{
    var input = new System.Text.StringBuilder();
    while (true)
    {
        var key = Console.ReadKey(intercept: true);
        if (key.Key == ConsoleKey.Enter)
        {
            Console.WriteLine();
            return input.ToString();
        }

        if (key.Key == ConsoleKey.Backspace)
        {
            if (input.Length > 0)
            {
                input.Length--;
                Console.Write("\b \b");
            }
            continue;
        }

        if (!char.IsControl(key.KeyChar))
        {
            input.Append(key.KeyChar);
            Console.Write('*');
        }
    }
}

// Schreibt einen Wert in die User Secrets eines Projekts, ohne ihn jemals über die eigene
// Konsolenausgabe laufen zu lassen (gleiches Vorgehen wie tuya-secrets-einrichten.sh).
static async Task SetUserSecretAsync(string projectPath, string key, string value)
{
    var startInfo = new ProcessStartInfo("dotnet")
    {
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        UseShellExecute = false,
    };
    startInfo.ArgumentList.Add("user-secrets");
    startInfo.ArgumentList.Add("set");
    startInfo.ArgumentList.Add(key);
    startInfo.ArgumentList.Add(value);
    startInfo.ArgumentList.Add("--project");
    startInfo.ArgumentList.Add(projectPath);

    using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("dotnet user-secrets konnte nicht gestartet werden.");
    await process.WaitForExitAsync();
    if (process.ExitCode != 0)
    {
        var error = await process.StandardError.ReadToEndAsync();
        throw new InvalidOperationException($"dotnet user-secrets set für \"{key}\" in \"{projectPath}\" ist fehlgeschlagen: {error}");
    }
}

static void PrintCameras(Dictionary<string, JsonElement> raw)
{
    // Felder, deren Werte HomeControl.Api.Blink.BlinkNetCameraClient.MapCameras bei Bedarf
    // auswerten könnte (Akku, Temperatur, Online-/Scharfstatus, Signalstärke). Nicht die
    // vollständigen Rohdaten ausgeben, nur diese gezielten Werte – weniger Angriffsfläche beim
    // Mitlesen/Kopieren und trotzdem genug, um die Zuordnung in MapCameras zu prüfen.
    string[] interestingFields = ["battery", "battery_state", "temp", "temperature", "status", "enabled", "armed", "wifi_strength"];

    foreach (var value in raw.Values)
    {
        if (value.ValueKind != JsonValueKind.Array)
        {
            continue;
        }

        foreach (var camera in value.EnumerateArray())
        {
            var name = camera.TryGetProperty("name", out var n) ? n.GetString() : "(ohne Namen)";
            Console.WriteLine($"  - Kamera \"{name}\"");
            Console.WriteLine($"    Feldnamen: {string.Join(", ", camera.EnumerateObject().Select(p => p.Name))}");

            foreach (var field in interestingFields)
            {
                if (camera.TryGetProperty(field, out var fieldValue))
                {
                    Console.WriteLine($"    {field} = {fieldValue.GetRawText()}");
                }
            }
        }
    }
}
