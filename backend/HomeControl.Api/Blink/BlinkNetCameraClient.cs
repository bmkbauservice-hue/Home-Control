using System.Text.Json;
using Blink;
using Blink.Exceptions;
using Microsoft.Extensions.Options;

namespace HomeControl.Api.Blink;

// Verbindet sich mit der Blink-Cloud über das NuGet-Paket Blink.NET (Version fest auf 1.0.9,
// siehe docs/BLINK.md: Blink hat keine offizielle API, alles ist reverse engineered).
//
// Singleton, anders als TuyaClient: Der BlinkClient aus Blink.NET hält selbst den Login-
// Zustand (Account-Id, interner HttpClient). Ein Neuanlegen pro Anfrage würde jedes Mal
// einen neuen Login erzwingen – bei einer inoffiziellen API ein unnötiges Sperrrisiko.
// Ein SemaphoreSlim verhindert, dass zwei Anfragen gleichzeitig einloggen oder abrufen.
//
// Schlägt der Login oder ein Abruf fehl, wird NICHT automatisch wiederholt: Blink.NET liefert
// keinen HTTP-Statuscode, deshalb lässt sich ein echtes 401 nicht sicher von einem kurzen
// Netzwerkfehler unterscheiden. Beides führt zum Status "Neu-Login nötig" (siehe docs/BLINK.md) –
// erst ein neuer Lauf von tools/BlinkSpike (mit 2FA) behebt das.
public class BlinkNetCameraClient(
    IServiceScopeFactory scopeFactory,
    IOptionsMonitor<BlinkOptions> options,
    ILogger<BlinkNetCameraClient> logger) : IBlinkCameraClient
{
    // Laut Blink.NET-Doku liefert der Server ohne Verzögerung zwischen Requests manchmal
    // leere Antworten. Für Hintergrundabrufe empfiehlt die Doku eher 5–10 Sekunden.
    private static readonly int GeneralSleepTimeMs = 5000;

    // Mindestabstand zwischen zwei echten Blink-Abrufen (Sperrschutz). Ein Klick auf
    // "Aktualisieren" innerhalb dieses Fensters bekommt den zwischengespeicherten Stand.
    private static readonly TimeSpan MinPollInterval = TimeSpan.FromMinutes(2);

    private readonly SemaphoreSlim gate = new(1, 1);

    private BlinkClient? client;
    private bool loginRequired;
    private DateTimeOffset lastFetchAtUtc = DateTimeOffset.MinValue;
    private IReadOnlyList<BlinkCameraInfo> lastCameras = [];

    public async Task<BlinkStatusResult> GetStatusAsync(CancellationToken cancellationToken)
    {
        try
        {
            await EnsureFreshAsync(cancellationToken);
            return new BlinkStatusResult(BlinkConnectionState.Connected, lastFetchAtUtc);
        }
        catch (BlinkNotConfiguredException)
        {
            return new BlinkStatusResult(BlinkConnectionState.NotConfigured, null);
        }
        catch (BlinkLoginRequiredException)
        {
            var lastSuccess = lastFetchAtUtc == DateTimeOffset.MinValue ? (DateTimeOffset?)null : lastFetchAtUtc;
            return new BlinkStatusResult(BlinkConnectionState.LoginRequired, lastSuccess);
        }
    }

    public async Task<IReadOnlyList<BlinkCameraInfo>> GetCamerasAsync(CancellationToken cancellationToken)
    {
        await EnsureFreshAsync(cancellationToken);
        return lastCameras;
    }

    // Sorgt dafür, dass lastCameras/lastFetchAtUtc höchstens MinPollInterval alt sind.
    // Wirft BlinkNotConfiguredException bzw. BlinkLoginRequiredException statt null/leer
    // zurückzugeben, damit die Endpunkte die passende ProblemDetails-Antwort wählen können.
    private async Task EnsureFreshAsync(CancellationToken cancellationToken)
    {
        if (IsFreshEnough())
        {
            if (loginRequired)
            {
                throw new BlinkLoginRequiredException("Blink-Anmeldung ist abgelaufen. Bitte tools/BlinkSpike erneut ausführen.");
            }

            return;
        }

        await gate.WaitAsync(cancellationToken);
        try
        {
            // Während des Wartens könnte eine parallele Anfrage den Cache schon gefüllt haben.
            if (IsFreshEnough())
            {
                if (loginRequired)
                {
                    throw new BlinkLoginRequiredException("Blink-Anmeldung ist abgelaufen. Bitte tools/BlinkSpike erneut ausführen.");
                }

                return;
            }

            await FetchAsync(cancellationToken);
        }
        finally
        {
            gate.Release();
        }
    }

    private bool IsFreshEnough() =>
        lastFetchAtUtc != DateTimeOffset.MinValue && DateTimeOffset.UtcNow - lastFetchAtUtc < MinPollInterval;

    private async Task FetchAsync(CancellationToken cancellationToken)
    {
        var activeClient = await EnsureLoggedInAsync(cancellationToken);

        try
        {
            var networks = await activeClient.GetNetworksAsync();
            var cameras = new List<BlinkCameraInfo>();

            foreach (var (networkIdText, _) in networks.Summary)
            {
                if (!int.TryParse(networkIdText, out var networkId))
                {
                    continue;
                }

                var raw = await activeClient.GetCamerasAsync(networkId);
                cameras.AddRange(MapCameras(raw));
            }

            lastCameras = cameras;
            lastFetchAtUtc = DateTimeOffset.UtcNow;
            loginRequired = false;
        }
        catch (BlinkClientException ex)
        {
            // Blink.NET liefert keinen HTTP-Status. Ein Fehler, nachdem wir gerade erst
            // erfolgreich eingeloggt waren, behandeln wir als abgelaufene Sitzung.
            logger.LogWarning(ex, "Blink-Abruf nach erfolgreichem Login fehlgeschlagen, Sitzung gilt als abgelaufen.");
            client = null;
            loginRequired = true;
            throw new BlinkLoginRequiredException("Blink hat den Abruf abgelehnt. Bitte tools/BlinkSpike erneut ausführen.");
        }
    }

    private async Task<BlinkClient> EnsureLoggedInAsync(CancellationToken cancellationToken)
    {
        if (client is not null && !loginRequired)
        {
            return client;
        }

        if (loginRequired)
        {
            throw new BlinkLoginRequiredException("Blink-Anmeldung ist abgelaufen. Bitte tools/BlinkSpike erneut ausführen.");
        }

        var missing = options.CurrentValue.GetMissingSettings();
        if (missing.Count > 0)
        {
            throw new BlinkNotConfiguredException($"Es fehlen folgende Einstellungen in den User Secrets: {string.Join(", ", missing)}.");
        }

        var credential = await LoadCredentialAsync(cancellationToken)
            ?? throw new BlinkNotConfiguredException("Blink ist noch nicht eingerichtet (kein Refresh-Token hinterlegt).");

        var newClient = new BlinkClient
        {
            GeneralSleepTime = GeneralSleepTimeMs,
            HardwareId = credential.HardwareId,
        };
        newClient.OnTokenRefreshed += newToken => PersistTokenFireAndForget(credential.HardwareId, newToken);

        var ok = await newClient.TryLoginWithRefreshTokenAsync(credential.RefreshToken);
        if (!ok)
        {
            loginRequired = true;
            throw new BlinkLoginRequiredException("Das gespeicherte Blink-Refresh-Token wurde abgelehnt. Bitte tools/BlinkSpike erneut ausführen.");
        }

        client = newClient;
        return newClient;
    }

    // OnTokenRefreshed ist ein reines Action<string>-Ereignis (kein async). Der Speichervorgang
    // läuft deshalb "fire and forget" im Hintergrund; ein Fehler dabei landet nur im Log,
    // denn ein neues Token ist trotzdem schon im laufenden BlinkClient aktiv.
    private void PersistTokenFireAndForget(string hardwareId, string refreshToken)
    {
        _ = Task.Run(async () =>
        {
            try
            {
                using var scope = scopeFactory.CreateScope();
                var store = scope.ServiceProvider.GetRequiredService<BlinkCredentialStore>();
                await store.SaveAsync(hardwareId, refreshToken, CancellationToken.None);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Neues Blink-Refresh-Token konnte nicht gespeichert werden.");
            }
        });
    }

    private async Task<(string HardwareId, string RefreshToken)?> LoadCredentialAsync(CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<BlinkCredentialStore>();
        return await store.GetCredentialAsync(cancellationToken);
    }

    // Die genauen Feldnamen je Kamera sind bei Blink nicht dokumentiert (keine offizielle API).
    // Diese Zuordnung ist "best effort" nach öffentlich bekannten Feldern (u. a. aus blinkpy)
    // und noch nicht gegen einen echten Account geprüft – siehe docs/BLINK.md, Abschnitt
    // "Bekannte Grenzen". Unbekannte oder fehlende Felder werden nie geraten, sondern bleiben null.
    private static IEnumerable<BlinkCameraInfo> MapCameras(Dictionary<string, JsonElement> raw)
    {
        foreach (var entry in FindCameraArray(raw))
        {
            var id = GetString(entry, "id") ?? GetNumberAsString(entry, "id") ?? string.Empty;
            if (id.Length == 0)
            {
                continue;
            }

            yield return new BlinkCameraInfo(
                Id: id,
                Name: GetString(entry, "name") ?? "Unbenannte Kamera",
                Battery: GetString(entry, "battery") ?? GetString(entry, "battery_state"),
                TemperatureCelsius: GetTemperatureCelsius(entry),
                Online: GetBool(entry, "enabled") ?? GetBool(entry, "armed"));
        }
    }

    // Blink antwortet je nach Endpunkt mit einem Array von Kameras oder einem Objekt, in dem
    // irgendeine Eigenschaft dieses Array enthält (z. B. "camera_status" oder "cameras").
    private static IEnumerable<JsonElement> FindCameraArray(Dictionary<string, JsonElement> raw)
    {
        foreach (var value in raw.Values)
        {
            if (value.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in value.EnumerateArray())
                {
                    yield return item;
                }
            }
        }
    }

    private static string? GetString(JsonElement element, string propertyName) =>
        element.ValueKind == JsonValueKind.Object
        && element.TryGetProperty(propertyName, out var value)
        && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static string? GetNumberAsString(JsonElement element, string propertyName) =>
        element.ValueKind == JsonValueKind.Object
        && element.TryGetProperty(propertyName, out var value)
        && value.ValueKind == JsonValueKind.Number
            ? value.ToString()
            : null;

    private static bool? GetBool(JsonElement element, string propertyName) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(propertyName, out var value)
            ? value.ValueKind switch
            {
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                _ => null,
            }
            : null;

    // Bekanntes Blink-Feld "temp" ist üblicherweise Fahrenheit. Da auch das nicht offiziell
    // dokumentiert ist, wird hier umgerechnet, aber in docs/BLINK.md als ungeprüft vermerkt.
    private static double? GetTemperatureCelsius(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        if (element.TryGetProperty("temp", out var temp) && temp.ValueKind == JsonValueKind.Number)
        {
            return Math.Round((temp.GetDouble() - 32) / 1.8, 1);
        }

        if (element.TryGetProperty("temperature", out var temperature) && temperature.ValueKind == JsonValueKind.Number)
        {
            return Math.Round((temperature.GetDouble() - 32) / 1.8, 1);
        }

        return null;
    }
}
