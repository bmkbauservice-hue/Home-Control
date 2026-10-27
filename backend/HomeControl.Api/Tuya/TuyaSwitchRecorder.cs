using System.Text.Json;
using HomeControl.Api.Data;
using HomeControl.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace HomeControl.Api.Tuya;

// Mögliche Ergebnisse beim Speichern des Tuya-Zustands. Als Text, damit sie im JSON lesbar sind.
public static class TuyaSyncResults
{
    // Zustand hat sich geändert: Gerät aktualisiert und Ereignis gespeichert
    public const string Changed = "changed";

    // Zustand ist gleich geblieben: nichts gespeichert
    public const string Unchanged = "unchanged";

    // Tuya:LinkedDeviceId ist nicht eingestellt: nichts gespeichert
    public const string NotLinked = "notLinked";

    // Das eingestellte Gerät gibt es in der Datenbank nicht (mehr) oder die Id ist ungültig:
    // nichts gespeichert
    public const string DeviceNotFound = "deviceNotFound";

    // switch_1 fehlt oder ist kein echter Boolean: nichts gespeichert, nichts geraten
    public const string UnknownValue = "unknownValue";
}

// Überträgt den von Tuya gemeldeten Schaltzustand (switch_1) auf ein Gerät in unserer Datenbank.
//
// - Nur echte Booleans zählen: true = EIN, false = AUS. Alles andere bleibt unbekannt.
// - Ändert sich der Zustand, werden IsActive und LastValue gesetzt und ein Ereignis
//   turned_on bzw. turned_off gespeichert.
// - Bleibt der Zustand gleich, wird nichts gespeichert. So füllt "Aktualisieren" den
//   Verlauf nicht mit doppelten Einträgen.
// - Es läuft immer nur ein Abgleich gleichzeitig. Sonst könnten zwei fast gleichzeitige
//   Abrufe (z. B. React StrictMode im Entwicklungsmodus) beide eine Änderung sehen und
//   dasselbe Ereignis doppelt speichern.
// - Es wird nur gelesen und gespeichert, nie geschaltet.
public class TuyaSwitchRecorder(ApplicationDbContext db)
{
    // static: gilt für alle Instanzen, denn der Recorder selbst ist "scoped" (pro Anfrage neu).
    private static readonly SemaphoreSlim RecordLock = new(1, 1);

    // Statuscode des Schaltkanals beim Testgerät
    public const string SwitchCode = "switch_1";

    // Texte für LastValue. Die Oberfläche zeigt sie direkt an.
    public const string OnText = "EIN";
    public const string OffText = "AUS";

    // linkedDeviceId ist der Text aus Tuya:LinkedDeviceId (leer = nicht verknüpft).
    public async Task<string> RecordAsync(
        string? linkedDeviceId,
        List<TuyaStatusItem> status,
        DateTimeOffset retrievedAtUtc,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(linkedDeviceId))
        {
            return TuyaSyncResults.NotLinked;
        }

        if (GetSwitchState(status) is not { } isOn)
        {
            return TuyaSyncResults.UnknownValue;
        }

        if (!Guid.TryParse(linkedDeviceId, out var deviceId))
        {
            return TuyaSyncResults.DeviceNotFound;
        }

        await RecordLock.WaitAsync(cancellationToken);
        try
        {
            return await RecordSwitchStateAsync(deviceId, isOn, retrievedAtUtc, cancellationToken);
        }
        finally
        {
            RecordLock.Release();
        }
    }

    // Vergleichen und Speichern. Läuft nur innerhalb von RecordLock.
    private async Task<string> RecordSwitchStateAsync(
        Guid deviceId,
        bool isOn,
        DateTimeOffset retrievedAtUtc,
        CancellationToken cancellationToken)
    {
        var device = await db.Devices.FirstOrDefaultAsync(d => d.Id == deviceId, cancellationToken);
        if (device is null)
        {
            return TuyaSyncResults.DeviceNotFound;
        }

        var newValue = isOn ? OnText : OffText;
        if (device.LastValue == newValue && device.IsActive == isOn)
        {
            return TuyaSyncResults.Unchanged;
        }

        device.IsActive = isOn;
        device.LastValue = newValue;

        // Zeitpunkt des Tuya-Abrufs, gekürzt auf Millisekunden wie bei allen Ereignissen
        // (siehe HasConversion im DbContext).
        db.DeviceEvents.Add(new DeviceEvent
        {
            DeviceId = device.Id,
            Action = isOn ? "turned_on" : "turned_off",
            Value = null,
            Timestamp = DateTimeOffset.FromUnixTimeMilliseconds(retrievedAtUtc.ToUnixTimeMilliseconds())
        });

        await db.SaveChangesAsync(cancellationToken);
        return TuyaSyncResults.Changed;
    }

    // true = EIN, false = AUS, null = unbekannt (fehlt oder kein echter Boolean)
    public static bool? GetSwitchState(List<TuyaStatusItem> status)
    {
        var item = status.FirstOrDefault(s => s.Code == SwitchCode);
        if (item is null)
        {
            return null;
        }

        return item.Value.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => null
        };
    }
}
