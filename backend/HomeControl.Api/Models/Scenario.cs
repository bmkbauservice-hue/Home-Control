namespace HomeControl.Api.Models;

// Eine Regel: "Wenn diese Tür aufgeht, schalte dieses Blink-System scharf."
// Neue Regeln starten immer ausgeschaltet.
public class Scenario
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string Name { get; set; } = string.Empty;

    public bool Enabled { get; set; }

    public Guid TriggerDeviceId { get; set; }

    public Device TriggerDevice { get; set; } = null!;

    public string BlinkSystemId { get; set; } = string.Empty;

    public string BlinkSystemName { get; set; } = string.Empty;

    public DateTimeOffset CreatedAtUtc { get; set; }

    public List<ScenarioRun> Runs { get; set; } = [];
}

// Ein Eintrag im Verlauf einer Regel.
public class ScenarioRun
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid ScenarioId { get; set; }

    public Scenario Scenario { get; set; } = null!;

    public DateTimeOffset StartedAtUtc { get; set; }

    public DateTimeOffset? FinishedAtUtc { get; set; }

    // "tuer" = echter Wechsel von Zu auf Auf, "test" = von Hand ausgelöst.
    public string Trigger { get; set; } = string.Empty;

    // Siehe ScenarioOutcomes.
    public string Outcome { get; set; } = string.Empty;

    public string Message { get; set; } = string.Empty;
}

public static class ScenarioOutcomes
{
    public const string Running = "laeuft";
    public const string Confirmed = "bestaetigt";
    public const string NotConfirmed = "nicht-bestaetigt";
    public const string Failed = "fehler";
}
