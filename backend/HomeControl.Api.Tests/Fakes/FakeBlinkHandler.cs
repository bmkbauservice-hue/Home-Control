using System.Net;
using System.Text;

namespace HomeControl.Api.Tests.Fakes;

// Ersetzt die Blink-Cloud im Test (wie FakeTuyaHandler für Tuya). Drei Adressen:
// - POST api.oauth.blink.com/oauth/token  → neues Access Token (TokenResponse)
// - GET  rest-prod…/users/tier_info        → Server und Account-Id (TierInfoResponse)
// - GET  rest-{tier}…/homescreen            → Kameras (HomescreenResponse)
// Jede Anfrage wird mitgeschrieben, damit Tests zählen und Header prüfen können.
public class FakeBlinkHandler : HttpMessageHandler
{
    private readonly Lock sync = new();
    private readonly List<string> tokenRequestBodies = [];
    private readonly List<HttpRequestMessage> homescreenRequests = [];
    private int tierInfoCount;

    // Bekommt die laufende Nummer des Token-Abrufs (ab 1). Standard: gültiges Token, neues Refresh-Token.
    public Func<int, (HttpStatusCode Status, string Body)> TokenResponse { get; set; } =
        n => (HttpStatusCode.OK, BlinkJson.Token($"access-{n}", $"refresh-{n}", 3600));

    public Func<(HttpStatusCode Status, string Body)> TierInfoResponse { get; set; } =
        () => (HttpStatusCode.OK, """{ "tier": "e006", "account_id": 4711 }""");

    // Bekommt die laufende Nummer des Homescreen-Abrufs (ab 1).
    public Func<int, (HttpStatusCode Status, string Body)> HomescreenResponse { get; set; } =
        _ => (HttpStatusCode.OK, BlinkJson.Homescreen());

    public IReadOnlyList<string> TokenRequestBodies { get { lock (sync) return tokenRequestBodies.ToList(); } }

    public IReadOnlyList<HttpRequestMessage> HomescreenRequests { get { lock (sync) return homescreenRequests.ToList(); } }

    public int TierInfoCount { get { lock (sync) return tierInfoCount; } }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var url = request.RequestUri!.ToString();

        if (url == "https://api.oauth.blink.com/oauth/token")
        {
            var body = await request.Content!.ReadAsStringAsync(cancellationToken);
            int number;
            lock (sync)
            {
                tokenRequestBodies.Add(body);
                number = tokenRequestBodies.Count;
            }
            // Kleine Pause: So überlappen parallele Aufrufe im Test wirklich.
            await Task.Delay(20, cancellationToken);
            return Respond(TokenResponse(number));
        }

        if (url == "https://rest-prod.immedia-semi.com/api/v1/users/tier_info")
        {
            lock (sync) tierInfoCount++;
            return Respond(TierInfoResponse());
        }

        if (url.EndsWith("/homescreen", StringComparison.Ordinal))
        {
            int number;
            lock (sync)
            {
                homescreenRequests.Add(request);
                number = homescreenRequests.Count;
            }
            return Respond(HomescreenResponse(number));
        }

        return new HttpResponseMessage(HttpStatusCode.NotFound);
    }

    private static HttpResponseMessage Respond((HttpStatusCode Status, string Body) answer) => new(answer.Status)
    {
        Content = new StringContent(answer.Body, Encoding.UTF8, "application/json"),
    };
}

// Antworten im Format der Blink-Cloud (nur die Felder, die wir lesen, plus ein paar zum Ignorieren).
public static class BlinkJson
{
    public static string Token(string accessToken, string? refreshToken, int expiresIn) => refreshToken is null
        ? $$"""{ "access_token": "{{accessToken}}", "expires_in": {{expiresIn}}, "token_type": "Bearer" }"""
        : $$"""{ "access_token": "{{accessToken}}", "refresh_token": "{{refreshToken}}", "expires_in": {{expiresIn}}, "token_type": "Bearer" }""";

    // Zwei normale Kameras und eine Mini. thumbnail und serial dürfen nie nach außen gelangen.
    public static string Homescreen() => """
        {
          "account": { "id": 4711 },
          "networks": [ { "id": 1, "name": "Muju Home" } ],
          "cameras": [
            { "id": 101, "name": "Garten", "network_id": 1, "battery": "ok", "serial": "GEHEIM-SERIAL", "thumbnail": "/media/e006/account/4711/network/1/camera/101/geheim" },
            { "id": 102, "name": "Einfahrt", "network_id": 1, "battery": "low" }
          ],
          "owls": [ { "id": 201, "name": "Flur Mini", "network_id": 1 } ],
          "doorbells": []
        }
        """;
}
