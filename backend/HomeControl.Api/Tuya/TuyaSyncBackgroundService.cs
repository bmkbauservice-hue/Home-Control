using HomeControl.Api.Data;
using HomeControl.Api.Status;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace HomeControl.Api.Tuya;

// Gleicht alle verknüpften Tuya-Geräte regelmäßig im Hintergrund ab, auch wenn niemand die App offen hat.
// Vorher passierte das nur beim Klick auf "Mit Tuya abgleichen": Eine geöffnete Tür blieb in der App
// "ZU", und das Ereignis door_opened ging verloren.
//
// BackgroundService: läuft einmal pro Backend-Prozess, startet mit der App und stoppt mit ihr.
// Nur lesend bei Tuya wie der Knopf auch, es wird nie geschaltet.
public class TuyaSyncBackgroundService(
    IServiceScopeFactory scopeFactory,
    IOptionsMonitor<TuyaOptions> options,
    TuyaOnlineCache onlineCache,
    TimeProvider timeProvider,
    ILogger<TuyaSyncBackgroundService> logger) : BackgroundService
{
    // Höchstens so lange wird nach Fehlern gewartet (Backoff), dann wieder regelmäßig versucht.
    public static readonly TimeSpan MaxBackoff = TimeSpan.FromHours(1);

    // Der Dienst wacht jede Minute auf und schaut, welche Geräte fällig sind (eigener Takt pro Gerät,
    // Device.TuyaSyncSeconds, sonst Tuya:AutoSyncSeconds). Ist keins fällig, wird Tuya NICHT gefragt.
    public static readonly TimeSpan Tick = TimeSpan.FromSeconds(TuyaOptions.MinAutoSyncSeconds);

    // Spielraum beim Vergleich: Der Tick kommt nie auf die Millisekunde genau. Ohne Spielraum würde aus
    // "alle 3 Minuten" manchmal "alle 4 Minuten".
    private static readonly TimeSpan DueTolerance = TimeSpan.FromSeconds(10);

    // Wann jedes Gerät zuletzt erfolgreich gelesen wurde (nur im Arbeitsspeicher; nach einem Neustart sind
    // alle Geräte sofort fällig). Nur der eine Hintergrund-Lauf schreibt hier, deshalb ohne Sperre.
    private readonly Dictionary<Guid, DateTimeOffset> lastSyncedAt = [];

    // Aktueller Abstand bis zum nächsten Durchlauf (für Log und Tests). null = Abgleich aus.
    public TimeSpan? CurrentPeriod { get; private set; }

    // Abstand nach "failures" Fehlversuchen in Folge: jedes Mal doppelt so lang, höchstens MaxBackoff.
    // 0 Fehler → normaler Takt, 1 → 2 ×, 2 → 4 ×, 3 → 8 × ...
    public static TimeSpan GetDelay(TimeSpan interval, int failures)
    {
        var delay = interval;
        for (var i = 0; i < failures && delay < MaxBackoff; i++)
        {
            delay *= 2;
        }

        return delay < MaxBackoff ? delay : MaxBackoff;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Gelesen beim Start. Ein geänderter Takt gilt nach einem Neustart des Dienstes.
        var settings = options.CurrentValue;
        var interval = settings.GetAutoSyncInterval();
        if (interval is null)
        {
            logger.LogInformation("Tuya-Hintergrund-Abgleich ist aus (Tuya:AutoSyncSeconds = 0).");
            return;
        }

        if (settings.AutoSyncSeconds < TuyaOptions.MinAutoSyncSeconds)
        {
            logger.LogWarning("Tuya:AutoSyncSeconds = {Seconds} ist zu klein, es wird alle {Minimum} Sekunden abgeglichen.",
                settings.AutoSyncSeconds, TuyaOptions.MinAutoSyncSeconds);
        }

        logger.LogInformation("Tuya-Hintergrund-Abgleich: Standard alle {Seconds} Sekunden, eigene Takte pro Gerät, Prüfung jede Minute.",
            interval.Value.TotalSeconds);
        CurrentPeriod = Tick;

        // PeriodicTimer: "tickt" in festen Abständen (hier jede Minute). Mit TimeProvider, damit Tests die
        // Zeit vorstellen können, statt wirklich zu warten.
        using var timer = new PeriodicTimer(Tick, timeProvider);
        var failures = 0;

        // Ab jetzt füllt dieser Dienst den Online-Speicher. GET /api/status fragt Tuya dann nicht mehr
        // selbst, sondern liest nur noch, was der letzte Durchlauf geholt hat (spart Kontingent).
        onlineCache.BackgroundRefreshActive = true;
        try
        {
            // Erster Durchlauf gleich beim Start: Was sich während eines Neustarts geändert hat, wird
            // sofort übernommen. Danach immer beim nächsten Tick.
            do
            {
                failures = await RunOnceAsync(stoppingToken) ? 0 : failures + 1;

                // Backoff: Nach Fehlern länger warten, damit wir eine gestörte Tuya-Cloud nicht fluten.
                var next = GetDelay(Tick, failures);
                if (next != timer.Period)
                {
                    timer.Period = next;
                    CurrentPeriod = next;
                    if (failures > 0)
                    {
                        logger.LogWarning("Tuya-Hintergrund-Abgleich: {Failures}. Fehler in Folge, nächster Versuch in {Minutes} Minuten.",
                            failures, next.TotalMinutes);
                    }
                }
            }
            while (await timer.WaitForNextTickAsync(stoppingToken));
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Normales Ende: Die App wird beendet. Kein Fehler.
        }
        finally
        {
            onlineCache.BackgroundRefreshActive = false;
        }
    }

    // Takt eines Geräts: eigener Wert (mindestens 60 s) oder der Standard.
    private static TimeSpan IntervalOf(int? seconds, TimeSpan defaultInterval) =>
        seconds is { } s ? TimeSpan.FromSeconds(Math.Max(s, TuyaOptions.MinAutoSyncSeconds)) : defaultInterval;

    // Ein Durchlauf. Gibt true zurück, wenn er geklappt hat (für den Backoff). Wirft nie eine
    // Exception, außer beim Beenden der App: Ein BackgroundService, der abstürzt, würde in .NET
    // sonst die ganze App beenden.
    public async Task<bool> RunOnceAsync(CancellationToken cancellationToken)
    {
        // Ohne Zugangsdaten nichts tun. Kein Fehler (kein Backoff): Tuya ist einfach nicht eingerichtet.
        // Bei jedem Durchlauf neu geprüft, so wirken nachgetragene Zugangsdaten ohne Neustart.
        if (options.CurrentValue.GetMissingCredentials().Count > 0)
        {
            logger.LogDebug("Tuya-Hintergrund-Abgleich übersprungen: Tuya ist nicht eingerichtet.");
            return true;
        }

        try
        {
            // TuyaDeviceSyncer und DbContext sind "scoped" (leben eine Anfrage lang). Dieser Dienst ist ein
            // Singleton ohne Anfrage, deshalb ein eigener Scope pro Durchlauf. Am Ende von using wird der
            // DbContext wieder freigegeben, es bleibt nichts vom letzten Durchlauf hängen.
            using var scope = scopeFactory.CreateScope();
            var syncer = scope.ServiceProvider.GetRequiredService<TuyaDeviceSyncer>();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

            // Welche verknüpften Geräte sind fällig? Eigener Takt (z. B. Tür 180 s) oder der Standard.
            var defaultInterval = options.CurrentValue.GetAutoSyncInterval() ?? Tick;
            var now = timeProvider.GetUtcNow();
            var linked = await db.Devices
                .Where(d => d.TuyaDeviceId != null && d.GoogleHomeDeviceId == null)
                .Select(d => new { d.Id, d.TuyaSyncSeconds })
                .ToListAsync(cancellationToken);
            var due = linked
                .Where(d => !lastSyncedAt.TryGetValue(d.Id, out var last)
                            || now - last >= IntervalOf(d.TuyaSyncSeconds, defaultInterval) - DueTolerance)
                .Select(d => d.Id)
                .ToList();
            if (linked.Count > 0 && due.Count == 0)
            {
                return true;   // Nichts fällig: keine Anfrage an Tuya.
            }

            // Online-Liste nur neu holen, wenn der letzte Stand älter als der Standard-Takt ist. Ein schneller
            // Lauf nur für die Tür kostet so genau einen Aufruf (Rechnung in docs/TUYA.md).
            var latest = onlineCache.Latest;
            var refreshOnline = latest is null || latest.LastAttemptFailed || now - latest.LoadedAtUtc >= defaultInterval - DueTolerance;

            // Dieselbe Logik wie der Knopf. Ihre Sperre (SyncLock) verhindert, dass beide gleichzeitig
            // laufen und ein Ereignis doppelt speichern.
            var results = await syncer.ReadAsync(apply: true, due, refreshOnline, cancellationToken);
            foreach (var result in results.Where(r => r.Error is null))
            {
                lastSyncedAt[result.DeviceId] = now;
            }

            var failed = results.Count(r => r.Error is not null);
            var changed = results.Count(r => r.SyncResult == TuyaSyncResults.Changed);
            // Ohne verknüpfte Geräte zählt nur, ob die Online-Liste geladen werden konnte.
            var nothingRead = results.Count > 0
                ? failed == results.Count
                : onlineCache.Latest?.LastAttemptFailed == true;
            if (nothingRead)
            {
                // Fehlermeldungen pro Gerät hat der Syncer schon geloggt (ohne Geheimnisse).
                logger.LogWarning("Tuya-Hintergrund-Abgleich: Kein Gerät konnte gelesen werden ({Count} Fehler).", failed);
                return false;
            }

            // Nur ins normale Log, wenn etwas passiert ist. Sonst stünde alle paar Minuten eine Zeile ohne Aussage im Journal.
            logger.Log(changed > 0 || failed > 0 ? LogLevel.Information : LogLevel.Debug,
                "Tuya-Hintergrund-Abgleich: {Count} Geräte gelesen, {Changed} geändert, {Failed} Fehler.",
                results.Count, changed, failed);
            return true;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            // Alles Unerwartete (z. B. Datenbank gesperrt): nur Typ und Meldung ins Log, nie Zugangsdaten.
            // Der Dienst läuft weiter und versucht es beim nächsten Tick erneut.
            logger.LogError("Tuya-Hintergrund-Abgleich fehlgeschlagen ({ExceptionType}): {Message}",
                ex.GetType().Name, ex.Message);
            return false;
        }
    }
}
