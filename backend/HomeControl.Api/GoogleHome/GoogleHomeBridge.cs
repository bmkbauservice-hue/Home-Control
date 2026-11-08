namespace HomeControl.Api.GoogleHome;

// Ein Gerät, wie Muju Home es gerade aus Google Home meldet. On = null: kein lesbarer Ein/Aus-Zustand.
public record BridgeDevice(string Id, string Name, bool Online, bool? On);

// Ein Schaltbefehl, den Muju Home abholt.
public record BridgeCommand(Guid Id, string DeviceId, bool On);

public enum CommandOutcome
{
    Confirmed,      // Das Gerät hat den gewünschten Zustand bestätigt.
    NotConfirmed,   // Befehl gesendet, gewünschter Zustand aber nicht gemeldet.
    DeviceOffline,
    DeviceUnknown,  // Muju Home kennt das Gerät nicht.
    Rejected,       // Google Home hat den Befehl abgelehnt.
    NotPickedUp,    // Innerhalb der Frist nicht abgeholt, wurde also nie ausgeführt.
    NoAnswer,       // Abgeholt, aber keine Rückmeldung bekommen. Zustand unbekannt.
    Busy,           // Für dieses Gerät läuft schon ein Befehl.
}

// Verbindet die Browser-Anfragen mit Muju Home. Die App fragt regelmäßig nach (Polling),
// denn Home Control kann das Handy nicht von sich aus erreichen.
//
// Alles liegt bewusst nur im Arbeitsspeicher: Nach einem Neustart gibt es keine alten Befehle mehr.
public class GoogleHomeBridge(TimeProvider timeProvider, IConfiguration config)
{
    private sealed class PendingCommand(Guid id, string deviceId, bool desired, DateTimeOffset pickupBefore)
    {
        public Guid Id { get; } = id;
        public string DeviceId { get; } = deviceId;
        public bool Desired { get; } = desired;
        public DateTimeOffset PickupBefore { get; } = pickupBefore;
        public bool Delivered { get; set; }
        public TaskCompletionSource<CommandOutcome> Result { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    private readonly Lock sync = new();
    private readonly List<PendingCommand> pending = [];
    private Dictionary<string, BridgeDevice> devices = [];
    private DateTimeOffset? lastPollUtc;

    // Wie lange seit der letzten Nachfrage gilt die App noch als verbunden?
    private TimeSpan ConnectedWindow => Seconds("GoogleHome:ConnectedSeconds", 6);

    // Bis wann muss ein Befehl abgeholt sein? Danach wird er nie mehr ausgeliefert.
    private TimeSpan PickupWindow => Seconds("GoogleHome:PickupSeconds", 4);

    // Gesamtzeit vom Klick bis zur Bestätigung durch das Gerät.
    private TimeSpan ConfirmWindow => Seconds("GoogleHome:ConfirmSeconds", 12);

    private TimeSpan Seconds(string key, double fallback) =>
        TimeSpan.FromSeconds(config.GetValue<double?>(key) ?? fallback);

    public bool IsConnected
    {
        get { lock (sync) return IsConnectedLocked(); }
    }

    private bool IsConnectedLocked() =>
        lastPollUtc is { } last && timeProvider.GetUtcNow() - last <= ConnectedWindow;

    // Alle gemeldeten Geräte. Ohne Verbindung leer, denn alte Meldungen sind nicht mehr verlässlich.
    public IReadOnlyList<BridgeDevice> Devices
    {
        get { lock (sync) return IsConnectedLocked() ? devices.Values.OrderBy(d => d.Name).ToList() : []; }
    }

    public BridgeDevice? Find(string deviceId)
    {
        lock (sync) return IsConnectedLocked() ? devices.GetValueOrDefault(deviceId) : null;
    }

    // Online-Stand je Google-Home-Gerät. null = App getrennt, also unbekannt.
    public IReadOnlyDictionary<string, bool?>? OnlineSnapshot()
    {
        lock (sync)
        {
            return IsConnectedLocked() ? devices.Values.ToDictionary(d => d.Id, d => (bool?)d.Online) : null;
        }
    }

    public bool HasPending(string deviceId)
    {
        lock (sync) return pending.Any(p => p.DeviceId == deviceId);
    }

    // Die App meldet ihre Geräte und bekommt dafür die offenen Befehle.
    // Jeder Befehl wird genau einmal ausgeliefert und nur innerhalb seiner Frist.
    public List<BridgeCommand> Poll(IEnumerable<BridgeDevice> reported)
    {
        var now = timeProvider.GetUtcNow();
        lock (sync)
        {
            devices = reported.ToDictionary(d => d.Id);
            lastPollUtc = now;

            var commands = new List<BridgeCommand>();
            foreach (var command in pending.Where(p => !p.Delivered))
            {
                if (now > command.PickupBefore)
                {
                    command.Result.TrySetResult(CommandOutcome.NotPickedUp);
                    continue;
                }
                command.Delivered = true;
                commands.Add(new BridgeCommand(command.Id, command.DeviceId, command.Desired));
            }
            return commands;
        }
    }

    // Stellt einen Befehl bereit und wartet auf die Bestätigung des Geräts.
    public async Task<CommandOutcome> SendAsync(string deviceId, bool desired, CancellationToken cancellationToken)
    {
        var pickup = PickupWindow;
        var command = new PendingCommand(Guid.NewGuid(), deviceId, desired, timeProvider.GetUtcNow() + pickup);
        lock (sync)
        {
            if (pending.Any(p => p.DeviceId == deviceId)) return CommandOutcome.Busy;
            pending.Add(command);
        }

        try
        {
            try
            {
                return await command.Result.Task.WaitAsync(pickup, timeProvider, cancellationToken);
            }
            catch (TimeoutException)
            {
                bool delivered;
                lock (sync) delivered = command.Delivered;
                if (!delivered) return CommandOutcome.NotPickedUp;
            }

            try
            {
                return await command.Result.Task.WaitAsync(ConfirmWindow - pickup, timeProvider, cancellationToken);
            }
            catch (TimeoutException)
            {
                return CommandOutcome.NoAnswer;
            }
        }
        finally
        {
            // Wer nicht mehr wartet, dessen Befehl darf auch nicht mehr ausgeführt werden.
            lock (sync) pending.Remove(command);
        }
    }

    // Rückmeldung der App. Verspätete Antworten finden keinen Befehl mehr und werden verworfen.
    public bool Complete(Guid commandId, bool? confirmedOn, string? error)
    {
        lock (sync)
        {
            var command = pending.FirstOrDefault(p => p.Id == commandId && p.Delivered);
            if (command is null) return false;

            var outcome = error switch
            {
                "offline" => CommandOutcome.DeviceOffline,
                "unknown" => CommandOutcome.DeviceUnknown,
                "rejected" => CommandOutcome.Rejected,
                _ => confirmedOn == command.Desired ? CommandOutcome.Confirmed : CommandOutcome.NotConfirmed,
            };
            return command.Result.TrySetResult(outcome);
        }
    }

    // Die PC-Steckdose ist ein reiner Messpunkt. Solche Geräte lassen sich gar nicht erst verknüpfen.
    public static bool IsProtectedName(string name)
    {
        var text = name.Trim().ToLowerInvariant();
        return text == "schlafzimmer" || text.Contains("pc-steckdose") || text.Contains("pc steckdose")
            || text.Contains("pc & technik") || text.Contains("pc und technik");
    }
}
