using System.Net;
using System.Text;

namespace HomeControl.Api.Tests.Fakes;

// Ersetzt die Tuya-Cloud im Test. Der HttpClient schickt seine Anfragen hierher statt ins Internet.
//
// Standardverhalten:
// - Token-Abruf Nr. n liefert "token-n" mit 7200 Sekunden Laufzeit
// - Statusabruf liefert switch_1 = false
// Beides lässt sich pro Test über TokenResponse und StatusResponse ändern.
public class FakeTuyaHandler : HttpMessageHandler
{
    private readonly Lock sync = new();
    private int tokenRequestCount;
    private readonly List<string?> statusAccessTokens = [];
    private readonly List<string> deviceListPaths = [];

    // Bekommt die laufende Nummer des Token-Abrufs (ab 1) und liefert den JSON-Body.
    public Func<int, string> TokenResponse { get; set; } = n => TuyaJson.Token($"token-{n}", 7200);

    // Bekommt die laufende Nummer des Statusabrufs (ab 1) und das mitgeschickte Token.
    public Func<int, string?, string> StatusResponse { get; set; } = (_, _) => TuyaJson.Status(false);

    // Bekommt die laufende Nummer des Geräteliste-Abrufs (ab 1) und liefert den JSON-Body.
    public Func<int, string> DeviceListResponse { get; set; } = _ => TuyaJson.DeviceList(false, null);

    // Optional: Token-Antworten erst schicken, wenn dieser Task fertig ist (für parallele Abrufe).
    public Task? TokenGate { get; set; }

    // Wird beim ersten Token-Abruf abgeschlossen. So weiß ein Test, dass der Abruf läuft.
    public TaskCompletionSource FirstTokenRequestStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public int TokenRequestCount
    {
        get { lock (sync) return tokenRequestCount; }
    }

    // Pfad mit Query jedes Geräteliste-Abrufs, in Reihenfolge
    public IReadOnlyList<string> DeviceListPaths
    {
        get { lock (sync) return deviceListPaths.ToList(); }
    }

    // access_token-Header jedes Statusabrufs, in Reihenfolge
    public IReadOnlyList<string?> StatusAccessTokens
    {
        get { lock (sync) return statusAccessTokens.ToList(); }
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var path = request.RequestUri!.PathAndQuery;

        if (path.StartsWith("/v1.0/token", StringComparison.Ordinal))
        {
            int number;
            lock (sync) number = ++tokenRequestCount;
            FirstTokenRequestStarted.TrySetResult();

            if (TokenGate is not null)
            {
                await TokenGate.WaitAsync(cancellationToken);
            }

            return Json(TokenResponse(number));
        }

        if (path.StartsWith("/v1.0/iot-01/associated-users/devices", StringComparison.Ordinal))
        {
            int number;
            lock (sync)
            {
                deviceListPaths.Add(path);
                number = deviceListPaths.Count;
            }

            return Json(DeviceListResponse(number));
        }

        if (path.StartsWith("/v1.0/iot-03/devices/", StringComparison.Ordinal))
        {
            var token = request.Headers.TryGetValues("access_token", out var values) ? values.Single() : null;
            int number;
            lock (sync)
            {
                statusAccessTokens.Add(token);
                number = statusAccessTokens.Count;
            }

            return Json(StatusResponse(number, token));
        }

        return new HttpResponseMessage(HttpStatusCode.NotFound);
    }

    // Tuya antwortet auch bei fachlichen Fehlern mit HTTP 200.
    private static HttpResponseMessage Json(string body) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(body, Encoding.UTF8, "application/json"),
    };
}

// Antworten im Format der Tuya-Cloud
public static class TuyaJson
{
    public static string Token(string accessToken, int? expireTime) => expireTime is null
        ? $$"""{ "success": true, "result": { "access_token": "{{accessToken}}" } }"""
        : $$"""{ "success": true, "result": { "access_token": "{{accessToken}}", "expire_time": {{expireTime}} } }""";

    public static string Status(bool switchOn) =>
        $$"""{ "success": true, "result": [ { "code": "switch_1", "value": {{(switchOn ? "true" : "false")}} } ] }""";

    // Eine Seite der Geräteliste. Jedes Gerät enthält wie bei Tuya auch einen local_key,
    // damit Tests prüfen können, dass er nirgends übernommen wird.
    public static string DeviceList(bool hasMore, string? lastRowKey, params (string Id, string Name, bool Online)[] devices)
    {
        var items = string.Join(",", devices.Select(d =>
            $$"""{ "id": "{{d.Id}}", "name": "{{d.Name}}", "product_name": "Wi-Fi Socket", "category": "cz", "online": {{(d.Online ? "true" : "false")}}, "local_key": "geheim-{{d.Id}}" }"""));
        var key = lastRowKey is null ? "null" : $"\"{lastRowKey}\"";
        return $$"""{ "success": true, "result": { "devices": [ {{items}} ], "has_more": {{(hasMore ? "true" : "false")}}, "last_row_key": {{key}} } }""";
    }

    public static string Error(string code, string message) =>
        $$"""{ "success": false, "code": {{code}}, "msg": "{{message}}" }""";
}
