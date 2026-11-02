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

// User-Secrets-Ids aus den .csproj-Dateien. Mit --id statt --project funktioniert das Schreiben
// unabhängig davon, aus welchem Ordner der Spike gestartet wird.
const string BackendSecretsId = "b10e47d4-d4f2-44f2-8fbb-5e1249af4daf";   // backend/HomeControl.Api
const string SpikeSecretsId = "a8cc534d-5044-4fa2-ad90-29fef9954a14";     // tools/BlinkSpike

// Version beim Start anzeigen: So ist sofort klar, welcher Stand des Spikes läuft.
Console.WriteLine("BlinkSpike, Stand 02.11.2026 (E-Mail-Abfrage, Secrets über stdin, feste HardwareId)");
Console.WriteLine("Optionen: --email-pruefen (nur prüfen, kein Login), --neue-email (E-Mail neu eingeben)");
Console.WriteLine();

// --email-pruefen: Zeigt nur Länge, Domain und Auffälligkeiten der gespeicherten E-Mail, nie die
// ganze Adresse, und loggt sich NICHT ein (zählt also nicht als Fehlversuch bei Blink).
if (args.Contains("--email-pruefen"))
{
    var stored = config["Blink:Email"];
    if (string.IsNullOrEmpty(stored))
    {
        Console.WriteLine("Keine E-Mail gespeichert. Beim nächsten normalen Start wird sie abgefragt.");
        return 0;
    }

    var at = stored.IndexOf('@');
    Console.WriteLine($"Gespeicherte E-Mail: {stored.Length} Zeichen, Domain \"{(at >= 0 ? stored[(at + 1)..] : "(kein @)")}\"");
    Console.WriteLine($"  Leerzeichen am Anfang/Ende: {(stored != stored.Trim() ? "JA (Fehlerquelle!)" : "nein")}");
    Console.WriteLine($"  Großbuchstaben enthalten:   {(stored != stored.ToLowerInvariant() ? "ja" : "nein")}");
    Console.WriteLine($"  Zeichen vor dem @:          {(at >= 0 ? at : stored.Length)}");
    return 0;
}

// Dieselbe HardwareId bei JEDEM Lauf, auch nach Fehlversuchen: Für Blink ist eine neue HardwareId ein
// neues Gerät. Deshalb wird sie beim allerersten Start erzeugt und SOFORT gespeichert, nicht erst nach
// einem erfolgreichen Login (das war ein Fehler: Jeder fehlgeschlagene Versuch hatte eine neue Id).
var savedHardwareId = config["Blink:HardwareId"];

// Früher hat der Spike hier zusätzlich eine eigene Kopie des Refresh-Tokens gespeichert
// (REVIEW.md M-4). Die ist nach dem ersten Login des Backends ohnehin veraltet, weil Blink das Token
// dabei austauscht, und wäre trotzdem ein Geheimnis im Klartext. Eine alte Kopie wird entfernt.
if (!string.IsNullOrWhiteSpace(config["Blink:RefreshToken"]))
{
    await RemoveUserSecretAsync(SpikeSecretsId, "Blink:RefreshToken");
    Console.WriteLine("Alte Kopie des Refresh-Tokens aus den User Secrets des Spikes entfernt.");
}

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
    Console.WriteLine($"HardwareId: {Mask(client.HardwareId)} (gespeichert, wird wiederverwendet)");
}
else
{
    // Blink.NET erzeugt beim Anlegen des Clients eine zufällige Id. Die merken wir uns sofort.
    await SetUserSecretsAsync(SpikeSecretsId, new() { ["Blink:HardwareId"] = client.HardwareId });
    Console.WriteLine($"HardwareId: {Mask(client.HardwareId)} (neu erzeugt und gespeichert, bleibt ab jetzt gleich)");
}

var loggedIn = false;

// Jeder Lauf ist ein Login mit E-Mail, Passwort und PIN (der Spike merkt sich kein Token mehr).
// Die E-Mail wird beim ersten Lauf abgefragt und in den User Secrets des Spikes gemerkt (über stdin,
// also nicht in der Shell-History). Kein Geheimnis, aber auch nichts für Git.
// --neue-email: gespeicherte Adresse ignorieren und neu abfragen (z. B. nach einem Tippfehler).
var email = args.Contains("--neue-email") ? null : config["Blink:Email"]?.Trim();
if (string.IsNullOrWhiteSpace(email))
{
    Console.Write("E-Mail-Adresse des Blink-Kontos: ");
    email = Console.ReadLine()?.Trim();
    if (string.IsNullOrWhiteSpace(email))
    {
        Console.WriteLine("Abgebrochen.");
        return 1;
    }
    await SetUserSecretsAsync(SpikeSecretsId, new() { ["Blink:Email"] = email });
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
    // Blink.NET meldet hier jede Antwort 400, 401 oder 403 der Blink-Anmeldeseite gleich. Das kann ein
    // falsches Passwort, eine falsche E-Mail oder eine vorübergehende Sperre nach Fehlversuchen sein.
    Console.WriteLine("Login fehlgeschlagen: Blink hat die Anmeldung abgelehnt.");
    Console.WriteLine("Mögliche Gründe: falsches Passwort, falsche E-Mail (prüfen mit --email-pruefen)");
    Console.WriteLine("oder eine vorübergehende Sperre nach mehreren Fehlversuchen.");
    // Laut Blink-Hilfe: nach 5 Fehlversuchen 10 Minuten gesperrt (beim 5. kommt eine Reset-E-Mail).
    Console.WriteLine("Bitte NICHT sofort wiederholen: erst in der Blink-App mit denselben Daten anmelden.");
    Console.WriteLine("Blink sperrt das Konto nach 5 Fehlversuchen für 10 Minuten. Sicherheitshalber");
    Console.WriteLine("15 Minuten warten (siehe docs/BLINK.md, \"Fehlversuche\").");
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

var refreshToken = client.RefreshToken;
if (string.IsNullOrWhiteSpace(refreshToken))
{
    Console.WriteLine("Unerwartet: kein Refresh-Token nach erfolgreicher Anmeldung erhalten.");
    return 1;
}

Console.WriteLine("Anmeldung erfolgreich.");

// Werte nie ausgeben: direkt in die User Secrets schreiben. Das Token nur ins Backend (als Startwert),
// in den Spike nur die HardwareId (kein Geheimnis).
await SetUserSecretsAsync(BackendSecretsId, new()
{
    ["Blink:HardwareId"] = client.HardwareId,
    ["Blink:InitialRefreshToken"] = refreshToken,
});
await SetUserSecretsAsync(SpikeSecretsId, new() { ["Blink:HardwareId"] = client.HardwareId });

Console.WriteLine("Übernommen: Blink:HardwareId und Blink:InitialRefreshToken in die User Secrets des Backends.");
Console.WriteLine();
Console.WriteLine("Nächste Schritte:");
Console.WriteLine("  1. Backend bzw. Dienst einmal /api/blink/status abrufen lassen (Übersicht öffnen).");
Console.WriteLine("     Das Token wird dabei verschlüsselt in die Datenbank übernommen.");
Console.WriteLine("  2. DANN den Startwert wieder entfernen (er wird nicht mehr gebraucht):");
Console.WriteLine("     dotnet user-secrets remove \"Blink:InitialRefreshToken\" --project backend/HomeControl.Api");
Console.WriteLine("     Solange er dort steht, mahnt das Backend im Log (siehe docs/BLINK.md).");
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

// Schreibt Werte in die User Secrets, ohne sie auszugeben und ohne sie als Kommandozeilenargument zu
// übergeben (REVIEW.md M-4): Argumente stehen während des Aufrufs in der Prozessliste (/proc/*/cmdline)
// und sind dort für andere Programme lesbar. Stattdessen geht ein JSON-Objekt über stdin an
// "dotnet user-secrets set", wie in tuya-secrets-einrichten.sh. Vorhandene andere Schlüssel bleiben stehen.
static async Task SetUserSecretsAsync(string secretsId, Dictionary<string, string> values)
{
    using var process = StartUserSecrets("set", secretsId, redirectInput: true);
    await process.StandardInput.WriteAsync(JsonSerializer.Serialize(values));
    process.StandardInput.Close();   // Ende der Eingabe: erst jetzt liest user-secrets das JSON
    await WaitForSuccessAsync(process, "set");
}

// Entfernt einen Schlüssel. Hier steht nur der NAME in der Kommandozeile, nie ein Wert.
static async Task RemoveUserSecretAsync(string secretsId, string key)
{
    using var process = StartUserSecrets("remove", secretsId, redirectInput: false, key);
    await WaitForSuccessAsync(process, "remove");
}

static Process StartUserSecrets(string command, string secretsId, bool redirectInput, string? key = null)
{
    var startInfo = new ProcessStartInfo("dotnet")
    {
        RedirectStandardInput = redirectInput,
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        UseShellExecute = false,
    };
    startInfo.ArgumentList.Add("user-secrets");
    startInfo.ArgumentList.Add(command);
    if (key is not null)
    {
        startInfo.ArgumentList.Add(key);
    }
    startInfo.ArgumentList.Add("--id");
    startInfo.ArgumentList.Add(secretsId);

    return Process.Start(startInfo) ?? throw new InvalidOperationException("dotnet user-secrets konnte nicht gestartet werden.");
}

static async Task WaitForSuccessAsync(Process process, string command)
{
    // Ausgaben lesen, damit der Prozess nicht an einem vollen Puffer hängen bleibt.
    var output = process.StandardOutput.ReadToEndAsync();
    var error = process.StandardError.ReadToEndAsync();
    await process.WaitForExitAsync();
    await output;
    if (process.ExitCode != 0)
    {
        throw new InvalidOperationException($"dotnet user-secrets {command} ist fehlgeschlagen: {await error}");
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
