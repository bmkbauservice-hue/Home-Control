using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;

namespace HomeControl.Api.Blink;

// Liest Kameras aus der Blink-Cloud mit einem eigenen, schlanken HttpClient (REVIEW.md M-5).
//
// Warum nicht mehr Blink.NET? Version 1.0.9 (die neueste) schickt bei jedem Abruf einen alten
// Android-App-Build mit und fragt die Kameras über /network/{id}/cameras ab. Blink antwortet darauf
// (Stand 02.11.2026) mit "426 Upgrade Required". Das aktuelle blinkpy liest stattdessen den
// "Homescreen" (v3) und schickt KEINEN App-Build-Header. Genau so macht es dieser Client.
// Blink.NET wird nur noch in tools/BlinkSpike für den Erst-Login mit 2FA gebraucht.
//
// Ablauf:
// 1. Access Token holen: Refresh-Token (aus BlinkCredentialStore) an api.oauth.blink.com schicken.
//    Blink gibt dabei meist ein NEUES Refresh-Token zurück; das wird sofort verschlüsselt gespeichert.
// 2. tier_info: Auf welchem Server (Tier, z. B. "e006") liegt das Konto, und welche Account-Id hat es?
// 3. Homescreen: alle Kameras (normale, Mini, Türklingeln) in EINER Antwort.
//
// Nur lesend: Es wird nie ein neues Bild oder Video angefordert, nie scharf/unscharf geschaltet.
//
// Singleton: Das Access Token und der letzte Stand bleiben zwischen den Anfragen erhalten.
public class BlinkHttpCameraClient(
    IHttpClientFactory httpClientFactory,
    IServiceScopeFactory scopeFactory,
    IOptionsMonitor<BlinkOptions> options,
    TimeProvider timeProvider,
    ILogger<BlinkHttpCameraClient> logger) : IBlinkCameraClient
{
    public const string HttpClientName = "Blink";

    // Werte wie in Blink.NET 1.0.9 und blinkpy (OAuth v2, iOS-Client). Damit hat der Login funktioniert.
    public const string TokenUrl = "https://api.oauth.blink.com/oauth/token";
    public const string TierInfoUrl = "https://rest-prod.immedia-semi.com/api/v1/users/tier_info";
    private const string OAuthClientId = "ios";
    private const string OAuthTokenUserAgent = "Blink/2511191620 CFNetwork/3860.200.71 Darwin/25.1.0";

    // Mindestabstand zwischen zwei echten Abrufen (Sperrschutz). Häufigere Aufrufe bekommen den
    // zwischengespeicherten Stand.
    public static readonly TimeSpan MinPollInterval = TimeSpan.FromMinutes(2);

    // Access Token so viel früher erneuern, damit es nicht mitten im Abruf abläuft.
    private static readonly TimeSpan RenewBeforeExpiry = TimeSpan.FromSeconds(60);

    // Der Tier landet im Hostnamen (rest-{tier}.immedia-semi.com). Nur Kleinbuchstaben, Ziffern und
    // Bindestrich zulassen, damit eine seltsame Antwort uns nie zu einem anderen Server schickt.
    private static readonly Regex TierPattern = new("^[a-z0-9]+(-[a-z0-9]+)*$", RegexOptions.Compiled);

    // Zwei Sperren: eine für das Token (nur EINE Erneuerung gleichzeitig, sonst würden zwei Anfragen
    // dasselbe Refresh-Token benutzen und eine davon bekäme 401), eine für den Abruf (Cache).
    private readonly SemaphoreSlim tokenLock = new(1, 1);
    private readonly SemaphoreSlim fetchLock = new(1, 1);

    private Session? session;

    // Re-Login-Schutz (siehe docs/BLINK.md): Hat Blink ein Refresh-Token abgelehnt, wird es nie wieder
    // benutzt. Erst ein neues Token (tools/BlinkSpike) löst einen neuen Versuch aus. Keine Schleife.
    private bool loginRequired;
    private string? lastFailedRefreshToken;

    private bool bootstrapWarningLogged;
    private bool fieldNamesLogged;

    private DateTimeOffset lastFetchAtUtc = DateTimeOffset.MinValue;
    private IReadOnlyList<BlinkCameraInfo> lastCameras = [];

    // Alles, was nach einer erfolgreichen Anmeldung bekannt ist. Bleibt nur im Arbeitsspeicher.
    private sealed record Session(
        string HardwareId,
        string RefreshToken,
        string AccessToken,
        DateTimeOffset AccessTokenValidUntil,
        string Tier,
        long AccountId);

    public async Task<BlinkStatusResult> GetStatusAsync(CancellationToken cancellationToken)
    {
        if (!options.CurrentValue.Enabled)
        {
            return new BlinkStatusResult(BlinkConnectionState.Disabled, null);
        }

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
        if (!options.CurrentValue.Enabled)
        {
            throw new BlinkDisabledException();
        }

        await EnsureFreshAsync(cancellationToken);
        return lastCameras;
    }

    private bool IsFreshEnough() =>
        !loginRequired
        && lastFetchAtUtc != DateTimeOffset.MinValue
        && timeProvider.GetUtcNow() - lastFetchAtUtc < MinPollInterval;

    private async Task EnsureFreshAsync(CancellationToken cancellationToken)
    {
        if (IsFreshEnough())
        {
            return;
        }

        await fetchLock.WaitAsync(cancellationToken);
        try
        {
            // Während des Wartens könnte eine parallele Anfrage den Stand schon geholt haben.
            if (IsFreshEnough())
            {
                return;
            }

            using var homescreen = await GetHomescreenAsync(cancellationToken);
            lastCameras = MapCameras(homescreen.RootElement);
            lastFetchAtUtc = timeProvider.GetUtcNow();
        }
        finally
        {
            fetchLock.Release();
        }
    }

    // Holt den Homescreen. Antwortet Blink mit 401 (Access Token abgelaufen oder widerrufen), wird
    // GENAU EINMAL ein neues Access Token geholt und der Abruf wiederholt. Ein zweites 401 ist ein
    // Fehler, keine Schleife.
    private async Task<JsonDocument> GetHomescreenAsync(CancellationToken cancellationToken)
    {
        var current = await GetSessionAsync(forceRenew: false, cancellationToken);
        using var first = await SendHomescreenRequestAsync(current, cancellationToken);
        if (first.StatusCode != HttpStatusCode.Unauthorized)
        {
            return await ReadHomescreenAsync(first, cancellationToken);
        }

        logger.LogInformation("Blink: Access Token abgelehnt (401), es wird einmal erneuert.");
        var renewed = await GetSessionAsync(forceRenew: true, cancellationToken);
        using var second = await SendHomescreenRequestAsync(renewed, cancellationToken);
        if (second.StatusCode == HttpStatusCode.Unauthorized)
        {
            // Auch mit frischem Token abgelehnt: Die Sitzung ist wirklich ungültig.
            MarkLoginRequired(renewed.RefreshToken);
            throw new BlinkLoginRequiredException("Blink hat die Anmeldung abgelehnt. Bitte tools/BlinkSpike erneut ausführen.");
        }

        return await ReadHomescreenAsync(second, cancellationToken);
    }

    private async Task<HttpResponseMessage> SendHomescreenRequestAsync(Session current, CancellationToken cancellationToken)
    {
        // Wie im aktuellen blinkpy: nur Bearer-Token und Accept, KEIN alter App-Build-Header.
        var url = $"https://rest-{current.Tier}.immedia-semi.com/api/v3/accounts/{current.AccountId}/homescreen";
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", current.AccessToken);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        return await httpClientFactory.CreateClient(HttpClientName).SendAsync(request, cancellationToken);
    }

    private static async Task<JsonDocument> ReadHomescreenAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.StatusCode == HttpStatusCode.UpgradeRequired)
        {
            // Kein Login-Problem: Blink lehnt die Art der Anfrage ab. Das Token bleibt gültig.
            throw new BlinkApiException("Blink verlangt eine neuere App-Version (426). Die inoffizielle Schnittstelle hat sich geändert, siehe docs/BLINK.md.");
        }

        if (!response.IsSuccessStatusCode)
        {
            throw new BlinkApiException($"Blink hat den Abruf der Kameras abgelehnt (HTTP {(int)response.StatusCode}).");
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        return await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
    }

    // Liefert eine gültige Sitzung. forceRenew: Access Token auf jeden Fall neu holen (nach 401).
    private async Task<Session> GetSessionAsync(bool forceRenew, CancellationToken cancellationToken)
    {
        var existing = session;
        if (!forceRenew && existing is not null && timeProvider.GetUtcNow() < existing.AccessTokenValidUntil - RenewBeforeExpiry)
        {
            return existing;
        }

        await tokenLock.WaitAsync(cancellationToken);
        try
        {
            // Hat eine parallele Anfrage gerade erneuert, deren Ergebnis benutzen statt noch einmal.
            if (session is not null && session != existing && timeProvider.GetUtcNow() < session.AccessTokenValidUntil - RenewBeforeExpiry)
            {
                return session;
            }

            session = await RenewAsync(cancellationToken);
            return session;
        }
        finally
        {
            tokenLock.Release();
        }
    }

    // Holt mit dem Refresh-Token ein neues Access Token. Nur innerhalb von tokenLock aufrufen.
    private async Task<Session> RenewAsync(CancellationToken cancellationToken)
    {
        var missing = options.CurrentValue.GetMissingSettings();
        if (missing.Count > 0)
        {
            throw new BlinkNotConfiguredException($"Es fehlen folgende Einstellungen in den User Secrets: {string.Join(", ", missing)}.");
        }

        // Vor JEDER Erneuerung das Token frisch aus der Datenbank lesen, nicht das aus dem Arbeitsspeicher
        // nehmen. Hat ein anderer Prozess auf derselben Datenbank (z. B. app-starten.sh neben dem Dienst)
        // das Token inzwischen erneuert, ist unser altes bei Blink schon ungültig, und wir übernehmen das
        // neuere. Ganz verhindern lässt sich das gleichzeitige Erneuern zweier Prozesse damit nicht,
        // deshalb gilt: ein Prozess pro Datenbank (docs/BLINK.md).
        var (hardwareId, refreshToken) = await LoadCredentialAsync(cancellationToken);
        if (session is { } known && known.RefreshToken != refreshToken)
        {
            logger.LogInformation("Blink: In der Datenbank liegt ein neueres Token (von einem anderen Prozess), es wird übernommen.");
        }

        if (loginRequired && refreshToken == lastFailedRefreshToken)
        {
            // Dasselbe, bereits abgelehnte Token: nicht noch einmal bei Blink fragen.
            throw new BlinkLoginRequiredException("Blink-Anmeldung ist abgelaufen. Bitte tools/BlinkSpike erneut ausführen.");
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, TokenUrl);
        request.Headers.TryAddWithoutValidation("User-Agent", OAuthTokenUserAgent);
        request.Headers.TryAddWithoutValidation("Accept", "*/*");
        request.Content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "refresh_token",
            ["refresh_token"] = refreshToken,
            ["client_id"] = OAuthClientId,
            ["scope"] = "client",
            ["hardware_id"] = hardwareId,
        });

        var http = httpClientFactory.CreateClient(HttpClientName);
        using var response = await http.SendAsync(request, cancellationToken);
        if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.BadRequest)
        {
            // Refresh-Token abgelehnt (abgelaufen, schon benutzt oder widerrufen). Nicht wiederholen.
            MarkLoginRequired(refreshToken);
            throw new BlinkLoginRequiredException("Blink hat das gespeicherte Refresh-Token abgelehnt. Bitte tools/BlinkSpike erneut ausführen.");
        }

        if (!response.IsSuccessStatusCode)
        {
            throw new BlinkApiException($"Blink-Anmeldung fehlgeschlagen (HTTP {(int)response.StatusCode}).");
        }

        using var tokenJson = await JsonDocument.ParseAsync(
            await response.Content.ReadAsStreamAsync(cancellationToken), cancellationToken: cancellationToken);
        var root = tokenJson.RootElement;
        var accessToken = GetString(root, "access_token")
            ?? throw new BlinkApiException("Blink hat kein Access Token geliefert.");
        var expiresIn = root.TryGetProperty("expires_in", out var e) && e.TryGetInt32(out var seconds) ? seconds : 0;
        var newRefreshToken = GetString(root, "refresh_token");

        // Blink tauscht das Refresh-Token meist aus (Rotation). Das alte ist danach ungültig. Deshalb
        // SOFORT verschlüsselt speichern, bevor irgendetwas anderes passiert. Der Wert kommt nie ins Log.
        if (!string.IsNullOrEmpty(newRefreshToken) && newRefreshToken != refreshToken)
        {
            await SaveRefreshTokenAsync(hardwareId, newRefreshToken, cancellationToken);
            refreshToken = newRefreshToken;
        }

        var (tier, accountId) = await GetTierInfoAsync(accessToken, cancellationToken);

        loginRequired = false;
        lastFailedRefreshToken = null;
        WarnIfBootstrapTokenStillConfigured();

        return new Session(hardwareId, refreshToken, accessToken,
            timeProvider.GetUtcNow().AddSeconds(expiresIn), tier, accountId);
    }

    private async Task<(string Tier, long AccountId)> GetTierInfoAsync(string accessToken, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, TierInfoUrl);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        using var response = await httpClientFactory.CreateClient(HttpClientName).SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new BlinkApiException($"Blink: Konto-Server (tier_info) nicht abrufbar (HTTP {(int)response.StatusCode}).");
        }

        using var json = await JsonDocument.ParseAsync(
            await response.Content.ReadAsStreamAsync(cancellationToken), cancellationToken: cancellationToken);
        var tier = GetString(json.RootElement, "tier");
        var accountId = json.RootElement.TryGetProperty("account_id", out var a) && a.TryGetInt64(out var id) ? id : 0;

        if (tier is null || !TierPattern.IsMatch(tier) || accountId <= 0)
        {
            throw new BlinkApiException("Blink: unerwartete Antwort von tier_info.");
        }

        return (tier, accountId);
    }

    private void MarkLoginRequired(string refreshToken)
    {
        loginRequired = true;
        lastFailedRefreshToken = refreshToken;
        session = null;
    }

    private async Task<(string HardwareId, string RefreshToken)> LoadCredentialAsync(CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<BlinkCredentialStore>();
        try
        {
            return await store.GetCredentialAsync(cancellationToken)
                ?? throw new BlinkNotConfiguredException("Blink ist noch nicht eingerichtet (kein Refresh-Token hinterlegt).");
        }
        catch (CryptographicException ex)
        {
            // Fehlender oder falscher Data-Protection-Schlüssel: Token nicht lesbar. Kein 500.
            logger.LogWarning("Gespeichertes Blink-Refresh-Token konnte nicht entschlüsselt werden ({ExceptionType}): {Message}",
                ex.GetType().Name, ex.Message);
            loginRequired = true;
            throw new BlinkLoginRequiredException(
                "Das gespeicherte Blink-Refresh-Token konnte nicht entschlüsselt werden. Bitte tools/BlinkSpike erneut ausführen.");
        }
    }

    private async Task SaveRefreshTokenAsync(string hardwareId, string refreshToken, CancellationToken cancellationToken)
    {
        try
        {
            using var scope = scopeFactory.CreateScope();
            var store = scope.ServiceProvider.GetRequiredService<BlinkCredentialStore>();
            await store.SaveAsync(hardwareId, refreshToken, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Das neue Token gilt trotzdem (im Arbeitsspeicher). Nach einem Neustart wäre aber ein neuer
            // Login mit 2FA nötig, deshalb als Fehler melden. Nur Typ und Meldung, nie der Wert.
            logger.LogError("Neues Blink-Refresh-Token konnte nicht gespeichert werden ({ExceptionType}): {Message}",
                ex.GetType().Name, ex.Message);
        }
    }

    // REVIEW.md M-4: Nach dem ersten erfolgreichen Login liegt das Token verschlüsselt in der Datenbank.
    // Der Startwert Blink:InitialRefreshToken steht dann noch im Klartext in den User Secrets. Das Backend
    // löscht ihn nicht selbst (die User Secrets gehören dem Entwickler), sondern mahnt deutlich im Log.
    private void WarnIfBootstrapTokenStillConfigured()
    {
        if (bootstrapWarningLogged || string.IsNullOrWhiteSpace(options.CurrentValue.InitialRefreshToken))
        {
            return;
        }

        bootstrapWarningLogged = true;
        logger.LogWarning(
            "Blink ist verbunden, das Token liegt verschlüsselt in der Datenbank. Bitte den Startwert aus den User Secrets " +
            "entfernen: dotnet user-secrets remove \"Blink:InitialRefreshToken\" --project backend/HomeControl.Api");
    }

    // Kameras aus dem Homescreen. Blink unterscheidet normale Kameras ("cameras"), Mini ("owls") und
    // Türklingeln ("doorbells"). Id, Name und Akku wie in blinkpy. Online und Temperatur bleiben null,
    // bis die echten Felder geprüft sind (siehe docs/BLINK.md, "Bekannte Grenzen").
    private List<BlinkCameraInfo> MapCameras(JsonElement homescreen)
    {
        LogFieldNamesOnce(homescreen);

        var cameras = new List<BlinkCameraInfo>();
        foreach (var listName in new[] { "cameras", "owls", "doorbells" })
        {
            if (!homescreen.TryGetProperty(listName, out var list) || list.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            foreach (var item in list.EnumerateArray())
            {
                var id = GetIdText(item);
                if (id is null)
                {
                    continue;
                }

                cameras.Add(new BlinkCameraInfo(
                    Id: id,
                    Name: GetString(item, "name") ?? "Unbenannte Kamera",
                    Battery: GetString(item, "battery") ?? GetString(item, "battery_state"),
                    TemperatureCelsius: null,
                    Online: null));
            }
        }

        return cameras;
    }

    // Einmal pro Programmstart: nur die NAMEN der Felder ins Log, nie Werte (keine Bild-Adressen,
    // keine Seriennummern). Damit lässt sich die Zuordnung an echten Daten prüfen, ohne zu raten.
    private void LogFieldNamesOnce(JsonElement homescreen)
    {
        if (fieldNamesLogged || homescreen.ValueKind != JsonValueKind.Object)
        {
            return;
        }

        fieldNamesLogged = true;
        var topLevel = homescreen.EnumerateObject().Select(p => p.Name).ToList();
        logger.LogInformation("Blink-Homescreen: Felder {Fields}", string.Join(", ", topLevel));
        foreach (var listName in new[] { "cameras", "owls", "doorbells" })
        {
            if (homescreen.TryGetProperty(listName, out var list) && list.ValueKind == JsonValueKind.Array
                && list.GetArrayLength() > 0 && list[0].ValueKind == JsonValueKind.Object)
            {
                logger.LogInformation("Blink-Homescreen: {List} ({Count} Stück), Felder je Eintrag: {Fields}",
                    listName, list.GetArrayLength(), string.Join(", ", list[0].EnumerateObject().Select(p => p.Name)));
            }
        }
    }

    private static string? GetIdText(JsonElement item) =>
        item.ValueKind == JsonValueKind.Object && item.TryGetProperty("id", out var id)
            ? id.ValueKind switch
            {
                JsonValueKind.Number => id.GetRawText(),
                JsonValueKind.String when !string.IsNullOrWhiteSpace(id.GetString()) => id.GetString(),
                _ => null,
            }
            : null;

    private static string? GetString(JsonElement element, string propertyName) =>
        element.ValueKind == JsonValueKind.Object
        && element.TryGetProperty(propertyName, out var value)
        && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
}
