using HomeControl.Api.Tuya;

namespace HomeControl.Api.Status;

// Merkt sich für 60 Sekunden, welche Tuya-Geräte online sind.
//
// Warum? Die Oberfläche fragt den Status regelmäßig ab (alle 60 Sekunden, auf jedem offenen
// Gerät). Ohne Zwischenspeicher wäre jede Abfrage ein Aufruf bei der Tuya-Cloud, und das
// Cloud-Projekt hat ein begrenztes Kontingent. Mit Zwischenspeicher fragt der Server höchstens
// einmal pro Minute, egal wie viele Geräte die Seite offen haben.
//
// Singleton: Der Zwischenspeicher muss zwischen den Anfragen erhalten bleiben. Den TuyaClient
// bekommt er deshalb nicht im Konstruktor (der ist kurzlebig), sondern als Funktion beim Aufruf.
public class TuyaOnlineCache(TimeProvider timeProvider)
{
    public static readonly TimeSpan MaxAge = TimeSpan.FromSeconds(60);

    // Verhindert, dass zwei gleichzeitige Anfragen beide bei Tuya nachfragen.
    private readonly SemaphoreSlim gate = new(1, 1);

    private IReadOnlyDictionary<string, bool?>? cached;
    private DateTimeOffset cachedAtUtc;

    // Liefert Tuya-Geräte-Id → online. Ist der gespeicherte Stand jünger als 60 Sekunden, wird
    // er benutzt, sonst wird über load neu geladen. Fehler beim Laden werden nicht gespeichert,
    // sondern an den Aufrufer weitergegeben (der nächste Aufruf versucht es dann erneut).
    public async Task<IReadOnlyDictionary<string, bool?>> GetAsync(
        Func<CancellationToken, Task<List<TuyaCloudDevice>>> load,
        CancellationToken cancellationToken)
    {
        await gate.WaitAsync(cancellationToken);
        try
        {
            var now = timeProvider.GetUtcNow();
            if (cached is not null && now - cachedAtUtc < MaxAge)
            {
                return cached;
            }

            var devices = await load(cancellationToken);
            var online = new Dictionary<string, bool?>();
            foreach (var device in devices)
            {
                online[device.Id] = device.Online;
            }

            cached = online;
            cachedAtUtc = now;
            return online;
        }
        finally
        {
            gate.Release();
        }
    }
}
