using HomeControl.Api.Tuya;

namespace HomeControl.Api.Status;

// Merkt sich, welche Tuya-Geräte online sind (eine Abfrage der Tuya-Geräteliste).
//
// Warum? Die Status-LEDs werden auf jedem offenen Gerät jede Minute abgefragt. Ohne Zwischenspeicher
// wäre jede Abfrage ein Aufruf bei der Tuya-Cloud, und das Kontingent ist begrenzt (Rechnung in
// docs/TUYA.md, "Hintergrund-Abgleich").
//
// Zwei Wege, ihn zu füllen:
// - RefreshAsync: immer frisch laden. Benutzt der Abgleich (Hintergrund-Dienst und Knopf).
// - GetAsync: nur laden, wenn der Stand älter als MaxAge ist. Benutzt GET /api/status, aber NUR, wenn
//   kein Hintergrund-Abgleich läuft (BackgroundRefreshActive). Läuft er, liest /api/status nur Latest
//   und fragt Tuya gar nicht selbst. So kosten die LEDs kein Kontingent, egal wie viele Geräte offen sind.
//
// Singleton: Der Zwischenspeicher muss zwischen den Anfragen erhalten bleiben. Den TuyaClient
// bekommt er deshalb nicht im Konstruktor (der ist kurzlebig), sondern als Funktion beim Aufruf.
public class TuyaOnlineCache(TimeProvider timeProvider)
{
    // Rückfallebene, wenn der Hintergrund-Abgleich aus ist: höchstens alle 5 Minuten bei Tuya fragen.
    public static readonly TimeSpan MaxAge = TimeSpan.FromMinutes(5);

    // Verhindert, dass zwei gleichzeitige Anfragen beide bei Tuya nachfragen.
    private readonly SemaphoreSlim gate = new(1, 1);

    // volatile: wird von verschiedenen Threads gelesen und geschrieben (Hintergrund-Dienst, Anfragen).
    private volatile TuyaOnlineSnapshot? latest;

    // Letzter Stand: online-Liste vom letzten ERFOLGREICHEN Laden, und ob der letzte Versuch danach
    // fehlgeschlagen ist. null = noch nie geladen.
    public TuyaOnlineSnapshot? Latest => latest;

    // true, solange der Hintergrund-Abgleich läuft (setzt TuyaSyncBackgroundService).
    public bool BackgroundRefreshActive { get; set; }

    // Liefert Tuya-Geräte-Id → online. Ist der gespeicherte Stand jünger als MaxAge, wird er benutzt,
    // sonst wird über load neu geladen. Fehler beim Laden werden an den Aufrufer weitergegeben.
    public async Task<IReadOnlyDictionary<string, bool?>> GetAsync(
        Func<CancellationToken, Task<List<TuyaCloudDevice>>> load,
        CancellationToken cancellationToken)
    {
        await gate.WaitAsync(cancellationToken);
        try
        {
            var current = latest;
            if (current is { LastAttemptFailed: false } && timeProvider.GetUtcNow() - current.LoadedAtUtc < MaxAge)
            {
                return current.Online;
            }

            return await LoadAsync(load, cancellationToken);
        }
        finally
        {
            gate.Release();
        }
    }

    // Lädt immer neu (ohne auf das Alter zu schauen). Fehler werden gemerkt und weitergegeben.
    public async Task<IReadOnlyDictionary<string, bool?>> RefreshAsync(
        Func<CancellationToken, Task<List<TuyaCloudDevice>>> load,
        CancellationToken cancellationToken)
    {
        await gate.WaitAsync(cancellationToken);
        try
        {
            return await LoadAsync(load, cancellationToken);
        }
        finally
        {
            gate.Release();
        }
    }

    // Nur innerhalb von gate aufrufen.
    private async Task<IReadOnlyDictionary<string, bool?>> LoadAsync(
        Func<CancellationToken, Task<List<TuyaCloudDevice>>> load,
        CancellationToken cancellationToken)
    {
        List<TuyaCloudDevice> devices;
        try
        {
            devices = await load(cancellationToken);
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            // Den alten Stand behalten, aber merken, dass Tuya gerade nicht antwortet (rote Tuya-LED).
            var previous = latest;
            latest = previous is null
                ? new TuyaOnlineSnapshot(new Dictionary<string, bool?>(), DateTimeOffset.MinValue, LastAttemptFailed: true)
                : previous with { LastAttemptFailed = true };
            throw;
        }

        var online = new Dictionary<string, bool?>();
        foreach (var device in devices)
        {
            online[device.Id] = device.Online;
        }

        latest = new TuyaOnlineSnapshot(online, timeProvider.GetUtcNow(), LastAttemptFailed: false);
        return online;
    }
}

// Ein Stand des Caches. LoadedAtUtc: wann die Liste zuletzt erfolgreich geladen wurde
// (DateTimeOffset.MinValue, wenn es nie geklappt hat).
public record TuyaOnlineSnapshot(
    IReadOnlyDictionary<string, bool?> Online,
    DateTimeOffset LoadedAtUtc,
    bool LastAttemptFailed);
