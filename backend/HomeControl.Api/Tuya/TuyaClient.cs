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
// Es werden keine Geheimnisse protokolliert. Das Token wird pro Abruf neu geholt
// und nirgends gespeichert.
//
// IOptionsMonitor liefert immer die aktuellen Einstellungen. So wirken geänderte
// User Secrets ohne Neustart der API.
public class TuyaClient(HttpClient http, IOptionsMonitor<TuyaOptions> options)
{

    // Aktuellen Status eines Geräts abrufen:
    // GET /v1.0/iot-03/devices/{device_id}/status
    public async Task<List<TuyaStatusItem>> GetDeviceStatusAsync(string deviceId, CancellationToken cancellationToken)
    {
        var accessToken = await GetAccessTokenAsync(cancellationToken);

        var path = $"/v1.0/iot-03/devices/{Uri.EscapeDataString(deviceId)}/status";
        var result = await SendGetAsync(path, accessToken, cancellationToken);

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

    // Token holen (einfacher Modus): GET /v1.0/token?grant_type=1
    private async Task<string> GetAccessTokenAsync(CancellationToken cancellationToken)
    {
        var result = await SendGetAsync("/v1.0/token?grant_type=1", accessToken: null, cancellationToken);

        return result.TryGetProperty("access_token", out var token) && token.GetString() is { Length: > 0 } value
            ? value
            : throw new JsonException("Tuya hat kein access_token geliefert.");
    }

    // Schickt eine signierte GET-Anfrage und gibt das Feld "result" der Antwort zurück.
    private async Task<JsonElement> SendGetAsync(string pathAndQuery, string? accessToken, CancellationToken cancellationToken)
    {
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString(CultureInfo.InvariantCulture);
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
