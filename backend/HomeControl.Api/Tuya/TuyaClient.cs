using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace HomeControl.Api.Tuya;

// Ein Statuswert genau so, wie Tuya ihn liefert, z. B. { "code": "switch_led", "value": true }.
// Value bleibt ein JsonElement, damit nichts umgewandelt oder erraten wird.
public record TuyaStatusItem(string Code, JsonElement Value);

// Tuya hat die Anfrage abgelehnt (z. B. falsche Zugangsdaten oder fehlende Berechtigung).
// Code und Meldung stammen von Tuya und enthalten keine Geheimnisse.
public class TuyaApiException(string tuyaCode, string tuyaMessage)
    : Exception($"Tuya-Fehler {tuyaCode}: {tuyaMessage}")
{
    public string TuyaCode { get; } = tuyaCode;

    public string TuyaMessage { get; } = tuyaMessage;
}

// Lesender Zugriff auf die Tuya-Cloud. Der HttpClient kommt per Dependency Injection
// (AddHttpClient in Program.cs) und hat die BaseUrl schon gesetzt.
//
// Der TuyaClient selbst ist "transient" (pro Anfrage neu). Das Zugriffstoken liegt deshalb
// im TuyaTokenStore (Singleton) und wird von dort wiederverwendet.
// Es werden keine Geheimnisse protokolliert.
//
// Nach einem Wechsel der Zugangsdaten wird die API neu gestartet. Zur Sicherheit gehört das
// gespeicherte Token zu einer ClientId (siehe TuyaTokenStore).
public class TuyaClient(
    HttpClient http,
    IOptionsMonitor<TuyaOptions> options,
    TuyaTokenStore tokenStore,
    TimeProvider timeProvider)
{
    // Tuya-Fehlercodes für ein abgelaufenes oder ungültiges Token (Tuya-Doku "Error codes"):
    // 1010 token is expired, 1011 token invalid, 1012 token status is invalid, 1400 token invalid
    private static readonly HashSet<string> TokenErrorCodes = ["1010", "1011", "1012", "1400"];

    // Aktuellen Status eines Geräts abrufen:
    // GET /v1.0/iot-03/devices/{device_id}/status
    public async Task<List<TuyaStatusItem>> GetDeviceStatusAsync(string deviceId, CancellationToken cancellationToken)
    {
        var path = $"/v1.0/iot-03/devices/{Uri.EscapeDataString(deviceId)}/status";
        var result = await SendWithTokenAsync(path, cancellationToken);

        if (result.ValueKind != JsonValueKind.Array)
        {
            throw new JsonException("Tuya hat für den Status keine Liste geliefert.");
        }

        return result.EnumerateArray()
            .Select(item => new TuyaStatusItem(
                item.GetProperty("code").GetString() ?? string.Empty,
                item.GetProperty("value").Clone()))
            .ToList();
    }

    // Schickt eine GET-Anfrage mit Token aus dem Speicher. Lehnt Tuya das Token ab
    // (success=false mit Token-Fehlercode), wird genau einmal mit einem neuen Token wiederholt.
    private async Task<JsonElement> SendWithTokenAsync(string pathAndQuery, CancellationToken cancellationToken)
    {
        var clientId = options.CurrentValue.ClientId;
        var accessToken = await tokenStore.GetAccessTokenAsync(clientId, FetchTokenAsync, cancellationToken);

        try
        {
            return await SendGetAsync(pathAndQuery, accessToken, cancellationToken);
        }
        catch (TuyaApiException ex) when (TokenErrorCodes.Contains(ex.TuyaCode))
        {
            var newToken = await tokenStore.GetAccessTokenAsync(
                clientId, FetchTokenAsync, cancellationToken, rejectedToken: accessToken);

            // Zweiter und letzter Versuch. Schlägt er fehl, geht der Fehler an den Aufrufer.
            return await SendGetAsync(pathAndQuery, newToken, cancellationToken);
        }
    }

    // Token holen (einfacher Modus): GET /v1.0/token?grant_type=1
    // Wird nur vom TuyaTokenStore aufgerufen, wenn kein gültiges Token vorliegt.
    private async Task<TuyaToken> FetchTokenAsync(CancellationToken cancellationToken)
    {
        var result = await SendGetAsync("/v1.0/token?grant_type=1", accessToken: null, cancellationToken);

        var accessToken = result.TryGetProperty("access_token", out var token) && token.GetString() is { Length: > 0 } value
            ? value
            : throw new JsonException("Tuya hat kein access_token geliefert.");

        // Laufzeit in Sekunden. Fehlt sie, gilt das Token als sofort abgelaufen
        // und wird beim nächsten Abruf neu geholt (wie ohne Cache).
        var expiresIn = result.TryGetProperty("expire_time", out var expire)
                && expire.ValueKind == JsonValueKind.Number
                && expire.TryGetInt32(out var seconds)
                && seconds > 0
            ? TimeSpan.FromSeconds(seconds)
            : TimeSpan.Zero;

        return new TuyaToken(accessToken, expiresIn);
    }

    // Schickt eine signierte GET-Anfrage und gibt das Feld "result" der Antwort zurück.
    private async Task<JsonElement> SendGetAsync(string pathAndQuery, string? accessToken, CancellationToken cancellationToken)
    {
        var timestamp = timeProvider.GetUtcNow().ToUnixTimeMilliseconds().ToString(CultureInfo.InvariantCulture);
        var nonce = Guid.NewGuid().ToString("N");
        var settings = options.CurrentValue;
        var sign = TuyaSignature.Sign(
            settings.ClientId, settings.ClientSecret, accessToken, timestamp, nonce, "GET", pathAndQuery);

        using var request = new HttpRequestMessage(HttpMethod.Get, pathAndQuery);
        request.Headers.Add("client_id", settings.ClientId);
        request.Headers.Add("sign", sign);
        request.Headers.Add("sign_method", "HMAC-SHA256");
        request.Headers.Add("t", timestamp);
        request.Headers.Add("nonce", nonce);
        if (accessToken is not null)
        {
            request.Headers.Add("access_token", accessToken);
        }

        using var response = await http.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new TuyaApiException($"HTTP {(int)response.StatusCode}", "Die Tuya-Cloud hat mit einem HTTP-Fehler geantwortet.");
        }

        // Tuya antwortet auch bei fachlichen Fehlern mit HTTP 200, dann aber mit "success": false.
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var json = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        var root = json.RootElement;

        var success = root.TryGetProperty("success", out var successElement) && successElement.ValueKind == JsonValueKind.True;
        if (!success)
        {
            var code = root.TryGetProperty("code", out var codeElement) ? codeElement.ToString() : "unbekannt";
            var message = root.TryGetProperty("msg", out var msgElement) ? msgElement.ToString() : "keine Meldung";
            throw new TuyaApiException(code, message);
        }

        if (!root.TryGetProperty("result", out var result))
        {
            throw new JsonException("Die Tuya-Antwort enthält kein Feld \"result\".");
        }

        // Clone, weil das JsonDocument am Ende dieser Methode freigegeben wird.
        return result.Clone();
    }
}
