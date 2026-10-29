using System.Security.Cryptography;
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
// Schlägt der Login oder ein Abruf fehl, wird NICHT automatisch bei Blink wiederholt:
// Blink.NET liefert keinen HTTP-Statuscode, deshalb lässt sich ein echtes 401 nicht sicher
// von einem kurzen Netzwerkfehler unterscheiden. Beides führt zum Status "Neu-Login nötig"
// (siehe docs/BLINK.md). Ein Neustart ist dafür aber NICHT nötig: Sobald in den User Secrets
// ein neuer Blink:InitialRefreshToken steht (z. B. nach einem erneuten Lauf von
// tools/BlinkSpike), wird er beim nächsten Aufruf automatisch übernommen und ein Login damit
// versucht (siehe BlinkCredentialStore und lastFailedRefreshToken unten).
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

    // Token, mit dem der letzte Login-Versuch fehlgeschlagen ist. Verhindert, dass bei jedem
    // Aufruf erneut mit demselben, bereits abgelehnten Token bei Blink angefragt wird – erst
    // ein ANDERER (neuer) Token-Wert löst einen neuen Versuch aus.
    private string? lastFailedRefreshToken;

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
            return;
        }

        await gate.WaitAsync(cancellationToken);
        try
        {
            // Während des Wartens könnte eine parallele Anfrage den Cache schon gefüllt haben.
            if (IsFreshEnough())
            {
                return;
            }

            await FetchAsync(cancellationToken);
        }
        finally
        {
            gate.Release();
        }
    }

    // Solange loginRequired gesetzt ist, zählt der Cache nicht: Jeder Aufruf soll sofort
    // prüfen, ob inzwischen ein neuer Bootstrap-Wert verfügbar ist (kein Neustart nötig).
    private bool IsFreshEnough() =>
        !loginRequired && lastFetchAtUtc != DateTimeOffset.MinValue && DateTimeOffset.UtcNow - lastFetchAtUtc < MinPollInterval;

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
            lastFailedRefreshToken = null;
        }
        catch (BlinkClientException ex)
        {
            // Blink.NET liefert keinen HTTP-Status. Ein Fehler, nachdem wir gerade erst
            // erfolgreich eingeloggt waren, behandeln wir als abgelaufene Sitzung.
            logger.LogWarning(
                "Blink-Abruf nach erfolgreichem Login fehlgeschlagen, Sitzung gilt als abgelaufen ({ExceptionType}): {Message}",
                ex.GetType().Name, ex.Message);
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

        var missing = options.CurrentValue.GetMissingSettings();
        if (missing.Count > 0)
        {
            throw new BlinkNotConfiguredException($"Es fehlen folgende Einstellungen in den User Secrets: {string.Join(", ", missing)}.");
        }

        var credential = await LoadCredentialAsync(cancellationToken)
            ?? throw new BlinkNotConfiguredException("Blink ist noch nicht eingerichtet (kein Refresh-Token hinterlegt).");

        if (loginRequired && credential.RefreshToken == lastFailedRefreshToken)
        {
            // Derselbe Token wie beim letzten fehlgeschlagenen Versuch: kein neuer Bootstrap-
            // Wert in den User Secrets, also nicht erneut bei Blink anfragen (Sperrschutz).
            throw new BlinkLoginRequiredException("Blink-Anmeldung ist abgelaufen. Bitte tools/BlinkSpike erneut ausführen.");
        }

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
            lastFailedRefreshToken = credential.RefreshToken;
            throw new BlinkLoginRequiredException("Das gespeicherte Blink-Refresh-Token wurde abgelehnt. Bitte tools/BlinkSpike erneut ausführen.");
        }

        client = newClient;
        loginRequired = false;
        lastFailedRefreshToken = null;
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
                logger.LogError(
                    "Neues Blink-Refresh-Token konnte nicht gespeichert werden ({ExceptionType}): {Message}",
                    ex.GetType().Name, ex.Message);
            }
        });
    }

    private async Task<(string HardwareId, string RefreshToken)?> LoadCredentialAsync(CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<BlinkCredentialStore>();

        try
        {
            return await store.GetCredentialAsync(cancellationToken);
        }
        catch (CryptographicException ex)
        {
            // Fehlender oder falscher Data-Protection-Schlüssel (siehe docs/BLINK.md): Das
            // gespeicherte Token ist nicht mehr lesbar. Kein 500, sondern derselbe Status wie
            // bei einem abgelehnten Login – ein neuer Lauf von tools/BlinkSpike behebt das.
            logger.LogWarning(
                "Gespeichertes Blink-Refresh-Token konnte nicht entschlüsselt werden ({ExceptionType}): {Message}",
                ex.GetType().Name, ex.Message);
            loginRequired = true;
            throw new BlinkLoginRequiredException(
                "Das gespeicherte Blink-Refresh-Token konnte nicht entschlüsselt werden (fehlender oder falscher Data-Protection-Schlüssel). Bitte tools/BlinkSpike erneut ausführen.");
        }
    }

    // Die genauen Feldnamen je Kamera sind bei Blink nicht dokumentiert (keine offizielle API).
    // Id, Name und Battery sind "best effort" nach öffentlich bekannten Feldern (u. a. aus
    // blinkpy). Online und TemperatureCelsius bleiben BEWUSST immer null: "enabled"/"armed"
    // sind Einstellungen (Bewegungserkennung bzw. Netzwerk scharf/unscharf), kein
    // Verbindungsstatus der Kamera – das als "online" auszugeben wäre irreführend. Beides wird
    // erst befüllt, wenn die echten Feldnamen anhand der Rohdaten aus tools/BlinkSpike
    // bestätigt sind, siehe docs/BLINK.md, Abschnitt "Bekannte Grenzen".
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
                TemperatureCelsius: null,
                Online: null);
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
}
