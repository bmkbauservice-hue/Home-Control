namespace HomeControl.Api.Tuya;

// Ein frisch von Tuya geholtes Token. ExpiresIn kommt aus "expire_time" (Sekunden).
public record TuyaToken(string AccessToken, TimeSpan ExpiresIn);

// Merkt sich das Tuya-Zugriffstoken zwischen den Anfragen (Singleton, siehe Program.cs).
//
// Warum eine eigene Klasse? AddHttpClient<TuyaClient> registriert den TuyaClient als
// "transient": Jede Anfrage bekommt einen neuen TuyaClient. Ein Feld im TuyaClient wäre
// danach wieder leer. Dieser Speicher lebt dagegen so lange wie die App.
//
// - Das Token wird 60 Sekunden vor Ablauf neu geholt (kein Refresh-Token-Ablauf).
// - Ein SemaphoreSlim sorgt dafür, dass bei parallelen Abrufen nur ein Token-Abruf läuft.
//   Die anderen warten und bekommen danach dasselbe Token.
// - Das Token merkt sich die ClientId. Passt sie nicht mehr zur Konfiguration, wird es
//   nicht mehr verwendet.
// - Die Uhrzeit kommt aus TimeProvider, damit Tests die Zeit vorspulen können.
// - Das Token liegt nur im Arbeitsspeicher und wird nie protokolliert. Ein Neustart leert es.
public class TuyaTokenStore(TimeProvider timeProvider)
{
    // So lange vor dem Ablauf wird schon ein neues Token geholt.
    public static readonly TimeSpan RenewBeforeExpiry = TimeSpan.FromSeconds(60);

    private readonly SemaphoreSlim fetchLock = new(1, 1);

    // Wird nur innerhalb von fetchLock geschrieben. volatile: andere Threads sehen sofort den neuen Wert.
    private volatile CachedToken? current;

    // Liefert ein gültiges Token für diese ClientId. Holt bei Bedarf ein neues über fetchToken.
    //
    // rejectedToken: Token, das Tuya gerade abgelehnt hat. Es wird nicht noch einmal ausgegeben.
    // Hat ein anderer Abruf inzwischen schon ein neues Token geholt, wird dieses verwendet.
    public async Task<string> GetAccessTokenAsync(
        string clientId,
        Func<CancellationToken, Task<TuyaToken>> fetchToken,
        CancellationToken cancellationToken,
        string? rejectedToken = null)
    {
        // Schneller Weg ohne Warten: Token vorhanden und gültig
        if (GetUsableToken(clientId, rejectedToken) is { } cached)
        {
            return cached;
        }

        await fetchLock.WaitAsync(cancellationToken);
        try
        {
            // Erneut prüfen: Während wir gewartet haben, kann ein anderer Abruf schon ein Token geholt haben.
            if (GetUsableToken(clientId, rejectedToken) is { } fetchedMeanwhile)
            {
                return fetchedMeanwhile;
            }

            // Zeit vor dem Abruf merken. So wird die Gültigkeit eher zu kurz als zu lang berechnet.
            var requestedAt = timeProvider.GetUtcNow();
            var token = await fetchToken(cancellationToken);

            current = new CachedToken(clientId, token.AccessToken, requestedAt + token.ExpiresIn);
            return token.AccessToken;
        }
        finally
        {
            fetchLock.Release();
        }
    }

    private string? GetUsableToken(string clientId, string? rejectedToken)
    {
        var token = current;
        if (token is null || token.ClientId != clientId || token.AccessToken == rejectedToken)
        {
            return null;
        }

        return timeProvider.GetUtcNow() < token.ExpiresAt - RenewBeforeExpiry ? token.AccessToken : null;
    }

    private record CachedToken(string ClientId, string AccessToken, DateTimeOffset ExpiresAt);
}
