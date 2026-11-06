using System.Threading.Channels;

namespace HomeControl.Api.Realtime;

// Welche Geräte sich geändert haben. Bewusst nur die Ids, keine Werte: Der Browser lädt danach über die normalen
// Endpunkte neu. So gibt es nur EINEN Weg zu den Daten (mit denselben Rechten), und eine verlorene Meldung schadet
// nicht, weil die nächste ohnehin alles neu holt.
public record DeviceChangeEvent(IReadOnlyList<Guid> DeviceIds, string Source, DateTimeOffset AtUtc);

// Woher eine Änderung kommt. Heute nur der Abgleich (Polling). Ein späterer Tuya-Push käme als weitere Quelle dazu.
public static class DeviceChangeSources
{
    public const string TuyaSync = "tuya-abgleich";
}

// Verteilt Änderungen an alle offenen Browser-Verbindungen (Publish/Subscribe im Arbeitsspeicher).
// Jede Verbindung bekommt ihre eigene kleine Warteschlange (Channel), damit ein langsamer Browser die anderen
// und den Abgleich nicht aufhält. Singleton: Es gibt genau eine Verteilstelle pro Backend-Prozess.
public class DeviceChangeHub
{
    // Obergrenze gegen zu viele offene Verbindungen (jede belegt etwas Arbeitsspeicher).
    public const int MaxSubscribers = 100;

    // Ist die Warteschlange voll, fällt die ÄLTESTE Meldung weg. Das ist unkritisch, siehe DeviceChangeEvent.
    private const int QueueCapacity = 32;

    private readonly Lock sync = new();
    private readonly List<Channel<DeviceChangeEvent>> subscribers = [];

    public int SubscriberCount
    {
        get { lock (sync) return subscribers.Count; }
    }

    // Neue Verbindung anmelden. null = zu viele Verbindungen. Dispose meldet sie wieder ab.
    public DeviceChangeSubscription? TrySubscribe()
    {
        var channel = Channel.CreateBounded<DeviceChangeEvent>(new BoundedChannelOptions(QueueCapacity)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
        });

        lock (sync)
        {
            if (subscribers.Count >= MaxSubscribers)
            {
                return null;
            }

            subscribers.Add(channel);
        }

        return new DeviceChangeSubscription(channel.Reader, () => Remove(channel));
    }

    // Blockiert nie: Mit DropOldest gelingt TryWrite immer, solange die Warteschlange offen ist.
    public void Publish(DeviceChangeEvent change)
    {
        lock (sync)
        {
            foreach (var subscriber in subscribers)
            {
                subscriber.Writer.TryWrite(change);
            }
        }
    }

    private void Remove(Channel<DeviceChangeEvent> channel)
    {
        lock (sync)
        {
            subscribers.Remove(channel);
        }

        channel.Writer.TryComplete();
    }
}

// Eine angemeldete Verbindung: Reader liest ihre Warteschlange, Dispose meldet sie ab.
public sealed class DeviceChangeSubscription(ChannelReader<DeviceChangeEvent> reader, Action unsubscribe) : IDisposable
{
    public ChannelReader<DeviceChangeEvent> Reader { get; } = reader;

    public void Dispose() => unsubscribe();
}
