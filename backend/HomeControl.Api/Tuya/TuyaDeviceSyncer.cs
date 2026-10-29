using System.Text.Json;
using HomeControl.Api.Data;
using HomeControl.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace HomeControl.Api.Tuya;

// Ergebnis für ein einzelnes Gerät beim Abgleich oder beim Auslesen.
// Status: die Rohwerte von Tuya (Codes und Werte unverändert). SyncResult: was in unserer
// Datenbank passiert ist (nur beim Abgleich). Error: warum das Gerät nicht gelesen werden konnte.
public record TuyaLinkedDeviceResult(
    Guid DeviceId,
    string Name,
    string Type,
    List<TuyaStatusItem>? Status,
    string? SyncResult,
    string? Error);

// Liest den Zustand ALLER Geräte, die mit einem Tuya-Gerät verknüpft sind (Device.TuyaDeviceId),
// und überträgt ihn auf unsere Geräte. Nur lesend bei Tuya, es wird nie geschaltet.
//
// Bekannte Statuscodes (Stand jetzt, weitere kommen, sobald wir die echten Werte der Geräte kennen):
// - switch_1 (Boolean): Steckdose/Licht, true = EIN
// - doorcontact_state (Boolean): Türkontakt, true = OFFEN
// Temperatur, Leistung (Watt) und Akku werden noch NICHT gespeichert: Tuya liefert dort ganze
// Zahlen mit einem Skalierungsfaktor, der je Gerät anders sein kann (z. B. 215 = 21,5 °C).
// Erst die echten Rohwerte ansehen (GET /api/tuya/linked-devices/status), dann abbilden. Nichts raten.
public class TuyaDeviceSyncer(ApplicationDbContext db, TuyaClient client, ILogger<TuyaDeviceSyncer> logger)
{
    // Ein Abgleich gleichzeitig, sonst könnten zwei fast gleichzeitige Aufrufe (z. B. StrictMode)
    // beide eine Änderung sehen und dasselbe Ereignis doppelt speichern.
    private static readonly SemaphoreSlim SyncLock = new(1, 1);

    // Obergrenze pro Aufruf. Jedes Gerät ist ein Aufruf bei Tuya (Kontingent des Cloud-Projekts).
    public const int MaxDevices = 50;

    public const string DoorCode = "doorcontact_state";

    public const string OpenText = "OFFEN";
    public const string ClosedText = "ZU";

    // Liest alle verknüpften Geräte bei Tuya. apply = false: nur ansehen, nichts speichern.
    public async Task<List<TuyaLinkedDeviceResult>> ReadAllAsync(bool apply, CancellationToken cancellationToken)
    {
        await SyncLock.WaitAsync(cancellationToken);
        try
        {
            var devices = await db.Devices
                .Where(d => d.TuyaDeviceId != null)
                .OrderBy(d => d.Name)
                .Take(MaxDevices)
                .ToListAsync(cancellationToken);

            var results = new List<TuyaLinkedDeviceResult>();
            var cloudUnreachable = false;

            foreach (var device in devices)
            {
                if (cloudUnreachable)
                {
                    // Nicht noch 40 Mal auf eine Zeitüberschreitung warten.
                    results.Add(new TuyaLinkedDeviceResult(device.Id, device.Name, device.Type, null, null,
                        "Nicht abgefragt, weil die Tuya-Cloud vorher nicht erreichbar war."));
                    continue;
                }

                try
                {
                    var status = await client.GetDeviceStatusAsync(device.TuyaDeviceId!, cancellationToken);
                    var syncResult = apply ? Apply(db, device, status, DateTimeOffset.UtcNow) : null;
                    results.Add(new TuyaLinkedDeviceResult(device.Id, device.Name, device.Type, status, syncResult, null));
                }
                catch (TuyaApiException ex)
                {
                    // Code und Meldung kommen von Tuya und enthalten keine Geheimnisse.
                    logger.LogWarning("Tuya hat die Anfrage für ein Gerät abgelehnt: {TuyaCode} {TuyaMessage}", ex.TuyaCode, ex.TuyaMessage);
                    results.Add(new TuyaLinkedDeviceResult(device.Id, device.Name, device.Type, null, null,
                        $"Tuya-Fehler {ex.TuyaCode}: {ex.TuyaMessage}"));
                }
                catch (HttpRequestException ex)
                {
                    logger.LogWarning("Tuya-Cloud nicht erreichbar: {Message}", ex.Message);
                    cloudUnreachable = true;
                    results.Add(new TuyaLinkedDeviceResult(device.Id, device.Name, device.Type, null, null,
                        "Die Tuya-Cloud ist nicht erreichbar."));
                }
                catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
                {
                    cloudUnreachable = true;
                    results.Add(new TuyaLinkedDeviceResult(device.Id, device.Name, device.Type, null, null,
                        "Die Tuya-Cloud antwortet nicht."));
                }
                catch (JsonException ex)
                {
                    logger.LogWarning("Unerwartete Antwort von Tuya: {Message}", ex.Message);
                    results.Add(new TuyaLinkedDeviceResult(device.Id, device.Name, device.Type, null, null,
                        "Unerwartete Antwort von Tuya."));
                }
            }

            if (apply)
            {
                await db.SaveChangesAsync(cancellationToken);
            }

            return results;
        }
        finally
        {
            SyncLock.Release();
        }
    }

    // Überträgt einen Status auf das Gerät. Ändert das Gerät und legt ein Ereignis an, speichert
    // aber NICHT (der Aufrufer ruft SaveChanges). Gibt ein Ergebnis aus TuyaSyncResults zurück:
    // Changed, Unchanged oder UnknownValue (kein bekannter Code oder kein echter Boolean).
    public static string Apply(
        ApplicationDbContext db,
        Device device,
        List<TuyaStatusItem> status,
        DateTimeOffset retrievedAtUtc)
    {
        // Zeitpunkt des Abrufs, gekürzt auf Millisekunden wie bei allen Ereignissen.
        var timestamp = DateTimeOffset.FromUnixTimeMilliseconds(retrievedAtUtc.ToUnixTimeMilliseconds());

        if (GetBoolean(status, TuyaSwitchRecorder.SwitchCode) is { } isOn)
        {
            var newValue = isOn ? TuyaSwitchRecorder.OnText : TuyaSwitchRecorder.OffText;
            if (device.LastValue == newValue && device.IsActive == isOn)
            {
                return TuyaSyncResults.Unchanged;
            }

            device.IsActive = isOn;
            device.LastValue = newValue;
            db.DeviceEvents.Add(new DeviceEvent
            {
                DeviceId = device.Id,
                Action = isOn ? "turned_on" : "turned_off",
                Timestamp = timestamp,
            });
            return TuyaSyncResults.Changed;
        }

        if (GetBoolean(status, DoorCode) is { } isOpen)
        {
            var newValue = isOpen ? OpenText : ClosedText;
            if (device.LastValue == newValue)
            {
                return TuyaSyncResults.Unchanged;
            }

            // IsActive bleibt bei Türkontakten unverändert: "aktiv" hat dort keine klare Bedeutung.
            device.LastValue = newValue;
            db.DeviceEvents.Add(new DeviceEvent
            {
                DeviceId = device.Id,
                Action = isOpen ? "door_opened" : "door_closed",
                Timestamp = timestamp,
            });
            return TuyaSyncResults.Changed;
        }

        return TuyaSyncResults.UnknownValue;
    }

    // true/false nur bei einem echten Boolean. Fehlt der Code oder ist der Wert etwas anderes
    // (Text, Zahl), kommt null: nichts geraten.
    public static bool? GetBoolean(List<TuyaStatusItem> status, string code)
    {
        var item = status.FirstOrDefault(s => s.Code == code);
        if (item is null)
        {
            return null;
        }

        return item.Value.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => null,
        };
    }
}
