using System.Text.Json;
using System.Threading.Channels;
using HomeControl.Api.Blink;
using HomeControl.Api.Data;
using HomeControl.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace HomeControl.Api.Scenarios;

// Auslöser: entweder eine Tür ging gerade auf, oder ein Admin testet eine bestimmte Regel.
public sealed record ScenarioTrigger(Guid? DoorDeviceId, Guid? ScenarioId, DateTimeOffset OccurredAtUtc);

// Warteschlange: Die App darf beim Melden der Tür nicht warten, bis Blink geantwortet hat.
public sealed class ScenarioQueue
{
    private readonly Channel<ScenarioTrigger> channel = Channel.CreateUnbounded<ScenarioTrigger>();

    public void Enqueue(ScenarioTrigger trigger) => channel.Writer.TryWrite(trigger);

    public IAsyncEnumerable<ScenarioTrigger> ReadAllAsync(CancellationToken cancellationToken) =>
        channel.Reader.ReadAllAsync(cancellationToken);
}

// Führt Regeln serverseitig aus, also auch bei geschlossenem Browser.
public sealed class ScenarioRunner(
    ScenarioQueue queue, IServiceScopeFactory scopes, TimeProvider timeProvider,
    IConfiguration config, ILogger<ScenarioRunner> logger) : BackgroundService
{
    private const int KeptRuns = 20;

    // Ein Auslöser, der so lange gewartet hat, ist kein frisches Signal mehr.
    private static readonly TimeSpan MaxTriggerAge = TimeSpan.FromMinutes(2);

    // Eine flatternde Tür löst innerhalb dieser Zeit nur einmal aus.
    private TimeSpan Cooldown => TimeSpan.FromSeconds(config.GetValue<double?>("Scenarios:CooldownSeconds") ?? 60);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await MarkInterruptedRunsAsync(stoppingToken);
        await foreach (var trigger in queue.ReadAllAsync(stoppingToken))
        {
            try
            {
                await ProcessAsync(trigger, stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError("Szenario konnte nicht ausgeführt werden ({ExceptionType}).", ex.GetType().Name);
            }
        }
    }

    // Läufe, die ein Neustart abgebrochen hat, dürfen nicht ewig als "läuft" stehen. Sie werden nicht wiederholt.
    private async Task MarkInterruptedRunsAsync(CancellationToken cancellationToken)
    {
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var open = await db.ScenarioRuns.Where(r => r.Outcome == ScenarioOutcomes.Running).ToListAsync(cancellationToken);
        foreach (var run in open)
        {
            run.Outcome = ScenarioOutcomes.Failed;
            run.Message = "Durch einen Neustart abgebrochen. Der Zustand von Blink ist unbekannt.";
            run.FinishedAtUtc = timeProvider.GetUtcNow();
        }
        await db.SaveChangesAsync(cancellationToken);
    }

    private async Task ProcessAsync(ScenarioTrigger trigger, CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        if (now - trigger.OccurredAtUtc > MaxTriggerAge) return;

        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var blink = scope.ServiceProvider.GetRequiredService<IBlinkCameraClient>();

        var query = trigger.ScenarioId is { } id
            ? db.Scenarios.Where(s => s.Id == id)
            : db.Scenarios.Where(s => s.Enabled && s.TriggerDeviceId == trigger.DoorDeviceId);

        foreach (var scenario in await query.Include(s => s.Runs).ToListAsync(cancellationToken))
        {
            var isDoor = trigger.DoorDeviceId is not null;
            if (isDoor && scenario.Runs.Any(r => r.Trigger == "tuer" && now - r.StartedAtUtc < Cooldown)) continue;
            await RunAsync(db, blink, scenario, isDoor ? "tuer" : "test", cancellationToken);
        }
    }

    private async Task RunAsync(
        ApplicationDbContext db, IBlinkCameraClient blink, Scenario scenario, string trigger, CancellationToken cancellationToken)
    {
        var run = new ScenarioRun
        {
            ScenarioId = scenario.Id,
            StartedAtUtc = timeProvider.GetUtcNow(),
            Trigger = trigger,
            Outcome = ScenarioOutcomes.Running,
            Message = "Blink wird scharfgeschaltet …",
        };
        db.ScenarioRuns.Add(run);
        await db.SaveChangesAsync(cancellationToken);

        (run.Outcome, run.Message) = await ArmAsync(blink, scenario, cancellationToken);
        run.FinishedAtUtc = timeProvider.GetUtcNow();

        // Der Verlauf bleibt kurz: Nur die letzten Läufe sind für die Anzeige nötig.
        foreach (var old in scenario.Runs.OrderByDescending(r => r.StartedAtUtc).Skip(KeptRuns))
        {
            db.ScenarioRuns.Remove(old);
        }
        await db.SaveChangesAsync(cancellationToken);
    }

    private async Task<(string Outcome, string Message)> ArmAsync(
        IBlinkCameraClient blink, Scenario scenario, CancellationToken cancellationToken)
    {
        try
        {
            var result = await blink.ArmSystemAsync(scenario.BlinkSystemId, cancellationToken);
            if (result is null)
                return (ScenarioOutcomes.Failed, "Blink kennt dieses System nicht mehr.");
            if (result.Armed != true)
                return (ScenarioOutcomes.NotConfirmed, result.CommandCompleted
                    ? "Befehl gesendet, aber Blink meldet das System nicht als scharf."
                    : "Blink hat den Befehl nicht rechtzeitig als erledigt gemeldet und das System nicht als scharf bestätigt.");
            return (ScenarioOutcomes.Confirmed, result.AlreadyArmed
                ? "War bereits scharf, kein Befehl nötig."
                : "Blink hat das Scharfschalten bestätigt.");
        }
        catch (Exception ex) when (ex is BlinkApiException or BlinkLoginRequiredException or BlinkNotConfiguredException
                                     or BlinkDisabledException or HttpRequestException or JsonException
                                     || (ex is TaskCanceledException && !cancellationToken.IsCancellationRequested))
        {
            logger.LogWarning("Szenario: Blink nicht scharfgeschaltet ({ExceptionType}).", ex.GetType().Name);
            return (ScenarioOutcomes.Failed, ex is BlinkDisabledException or BlinkLoginRequiredException or BlinkNotConfiguredException or BlinkApiException
                ? ex.Message
                : "Die Blink-Cloud ist nicht erreichbar. Nichts wurde bestätigt.");
        }
    }
}
