using System.Text.Json;
using HomeControl.Api.Data;
using HomeControl.Api.Models;
using HomeControl.Api.Realtime;
using HomeControl.Api.Status;
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
// Bekannte Statuscodes (aus den echten Rohwerten unserer Geräte, tuya-rohwerte-anzeigen.sh):
// - switch_1 (Boolean): Steckdose/Licht, true = EIN                         → Ereignis
// - doorcontact_state (Boolean): Türkontakt, true = OFFEN                   → Ereignis
// - cur_power (Zahl): Leistung in Zehntel Watt, 1234 = 123,4 W              → nur letzter Wert
// - cur_voltage (Zahl): Spannung in Zehntel Volt, 2271 = 227,1 V            → nur letzter Wert
// - battery_percentage (Zahl): Akku in Prozent, 83 = 83 %                    → nur letzter Wert
// Tuya liefert Messwerte als ganze Zahlen mit Skalierungsfaktor. Den Faktor nicht raten, sondern an
// echten Rohwerten prüfen. Temperatur fehlt noch: Kein verknüpftes Gerät liefert bisher einen Wert.
public class TuyaDeviceSyncer(
    ApplicationDbContext db,
    TuyaClient client,
    TuyaOnlineCache onlineCache,
    DeviceChangeHub changeHub,
    ILogger<TuyaDeviceSyncer> logger)
{
    // Ein Abgleich gleichzeitig, sonst könnten zwei fast gleichzeitige Aufrufe (z. B. StrictMode)
    // beide eine Änderung sehen und dasselbe Ereignis doppelt speichern.
    private static readonly SemaphoreSlim SyncLock = new(1, 1);

    // Obergrenze pro Aufruf. Jedes Gerät ist ein Aufruf bei Tuya (Kontingent des Cloud-Projekts).
    public const int MaxDevices = 50;

    public const string DoorCode = "doorcontact_state";

    public const string PowerCode = "cur_power";
    public const string VoltageCode = "cur_voltage";
    public const string BatteryCode = "battery_percentage";

    // Skalierung: Rohwert / Faktor = echter Wert.
    // Spannung geprüft am Barlicht (2271 → 227,1 V passt zum Stromnetz). Leistung nach der
    // Tuya-Konvention für Steckdosen mit Strommesser; einmal mit bekannter Last nachprüfen.
    public const double PowerScale = 10;
    public const double VoltageScale = 10;

    public const string OpenText = "OFFEN";
    public const string ClosedText = "ZU";

    // Liest alle verknüpften Geräte bei Tuya. apply = false: nur ansehen, nichts speichern.
    // Benutzt vom Knopf "Mit Tuya abgleichen" und von GET /api/tuya/linked-devices/status.
    public Task<List<TuyaLinkedDeviceResult>> ReadAllAsync(bool apply, CancellationToken cancellationToken) =>
        ReadAsync(apply, onlyDeviceIds: null, refreshOnline: true, cancellationToken);

    // Wie oben, aber nur für bestimmte Geräte (Hintergrund-Abgleich: nur die gerade fälligen, z. B. die Tür
    // alle 3 Minuten). refreshOnline = false: die Online-Liste NICHT neu bei Tuya holen, sondern den letzten
    // Stand aus dem TuyaOnlineCache nehmen. So kostet ein Lauf nur für die Tür genau einen Tuya-Aufruf.
    public async Task<List<TuyaLinkedDeviceResult>> ReadAsync(
        bool apply,
        IReadOnlyCollection<Guid>? onlyDeviceIds,
        bool refreshOnline,
        CancellationToken cancellationToken)
    {
        await SyncLock.WaitAsync(cancellationToken);
        try
        {
            var query = db.Devices.Where(d => d.TuyaDeviceId != null);
            if (onlyDeviceIds is not null)
            {
                query = query.Where(d => onlyDeviceIds.Contains(d.Id));
            }

            var devices = await query
                .OrderBy(d => d.Name)
                .Take(MaxDevices)
                .ToListAsync(cancellationToken);

            var results = new List<TuyaLinkedDeviceResult>();
            var cloudUnreachable = false;

            // Online/Offline aller Geräte aus EINER Abfrage, immer frisch. Das Ergebnis landet im selben
            // Speicher, aus dem die Status-LEDs lesen (TuyaOnlineCache). Nur beim Speichern nötig, siehe Apply.
            var onlineBefore = onlineCache.Latest?.Online;
            var online = !apply ? null : refreshOnline ? await LoadOnlineAsync(cancellationToken) : CachedOnline();

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
                    var syncResult = apply
                        ? Apply(db, device, status, DateTimeOffset.UtcNow, IsOnline(online, device.TuyaDeviceId!))
                        : null;
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
                var changedIds = FindChangedDevices(devices, onlineBefore, online);
                await db.SaveChangesAsync(cancellationToken);

                // Erst NACH dem Speichern melden: Der Browser lädt sofort neu und soll die neuen Werte bekommen.
                if (changedIds.Count > 0)
                {
                    changeHub.Publish(new DeviceChangeEvent(changedIds, DeviceChangeSources.TuyaSync, DateTimeOffset.UtcNow));
                }
            }

            return results;
        }
        finally
        {
            SyncLock.Release();
        }
    }

    // Geräte, bei denen der Browser neu laden sollte: EF Core weiß, welche Zeilen sich wirklich geändert haben
    // (gleicher Wert zählt nicht), dazu Geräte, deren Online-Zustand gewechselt hat (der steht nicht in der Tabelle).
    private List<Guid> FindChangedDevices(
        List<Device> devices,
        IReadOnlyDictionary<string, bool?>? onlineBefore,
        IReadOnlyDictionary<string, bool?>? onlineNow)
    {
        var changed = db.ChangeTracker.Entries<Device>()
            .Where(entry => entry.State == EntityState.Modified)
            .Select(entry => entry.Entity.Id)
            .ToHashSet();

        if (onlineNow is not null)
        {
            foreach (var device in devices)
            {
                if (IsOnline(onlineBefore, device.TuyaDeviceId!) != IsOnline(onlineNow, device.TuyaDeviceId!))
                {
                    changed.Add(device.Id);
                }
            }
        }

        return changed.ToList();
    }

    // Online-Zustand je Tuya-Id. null, wenn die Liste gerade nicht abrufbar ist: Dann wird so
    // gespeichert wie bisher (siehe Apply), der Abgleich scheitert nicht daran.
    private async Task<IReadOnlyDictionary<string, bool?>?> LoadOnlineAsync(CancellationToken cancellationToken)
    {
        try
        {
            return await onlineCache.RefreshAsync(client.GetLinkedAppDevicesAsync, cancellationToken);
        }
        catch (Exception ex) when (ex is TuyaApiException or HttpRequestException or JsonException
                                       || (ex is TaskCanceledException && !cancellationToken.IsCancellationRequested))
        {
            logger.LogWarning("Online-Zustand von Tuya nicht abrufbar: {Message}", ex.Message);
            return null;
        }
    }

    // Letzter Stand aus dem Speicher, ohne Tuya zu fragen. null, wenn es keinen gültigen Stand gibt.
    private IReadOnlyDictionary<string, bool?>? CachedOnline() =>
        onlineCache.Latest is { LastAttemptFailed: false } latest ? latest.Online : null;

    // true/false, wenn Tuya es weiß. null = unbekannt (Liste nicht abrufbar oder Gerät nicht darin).
    private static bool? IsOnline(IReadOnlyDictionary<string, bool?>? online, string tuyaDeviceId) =>
        online is not null && online.TryGetValue(tuyaDeviceId, out var value) ? value : null;

    // Überträgt einen Status auf das Gerät. Ändert das Gerät und legt ein Ereignis an, speichert
    // aber NICHT (der Aufrufer ruft SaveChanges). Gibt ein Ergebnis aus TuyaSyncResults zurück:
    // Changed, Unchanged, Measured (nur Messwerte) oder UnknownValue (kein bekannter Code).
    //
    // online = false: Tuya liefert für Offline-Geräte den LETZTEN bekannten Stand. Die Messwerte
    // werden dann nicht übernommen, sonst sähe ein alter Wert wie ein neuer aus.
    public static string Apply(
        ApplicationDbContext db,
        Device device,
        List<TuyaStatusItem> status,
        DateTimeOffset retrievedAtUtc,
        bool? online = null)
    {
        // Zeitpunkt des Abrufs, gekürzt auf Millisekunden wie bei allen Ereignissen.
        var timestamp = DateTimeOffset.FromUnixTimeMilliseconds(retrievedAtUtc.ToUnixTimeMilliseconds());

        // Messwerte zuerst und unabhängig vom Schalter: Eine Steckdose mit Strommesser hat beides.
        var measured = online != false && ApplyMeasurements(device, status, timestamp);

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

        return measured ? TuyaSyncResults.Measured : TuyaSyncResults.UnknownValue;
    }

    // Überträgt Leistung, Spannung und Akku, soweit vorhanden und plausibel. Bewusst KEIN Ereignis:
    // Die Leistung schwankt ständig, der Verlauf wäre sonst voller Einträge ohne Aussage.
    // Gibt true zurück, wenn mindestens ein Messwert übernommen wurde.
    public static bool ApplyMeasurements(Device device, List<TuyaStatusItem> status, DateTimeOffset timestamp)
    {
        var found = false;

        if (GetNumber(status, PowerCode) is { } power && power >= 0)
        {
            device.PowerWatt = power / PowerScale;
            found = true;
        }

        if (GetNumber(status, VoltageCode) is { } voltage && voltage >= 0)
        {
            device.VoltageVolt = voltage / VoltageScale;
            found = true;
        }

        if (GetNumber(status, BatteryCode) is { } battery && battery is >= 0 and <= 100)
        {
            device.BatteryPercent = (int)battery;
            found = true;
        }

        if (found)
        {
            device.MeasuredAtUtc = timestamp;
        }

        return found;
    }

    // Ganze Zahl nur bei einer echten JSON-Zahl ohne Nachkommastellen. Text ("83"), Boolean oder
    // Kommazahl ergeben null: nichts geraten.
    public static long? GetNumber(List<TuyaStatusItem> status, string code)
    {
        var item = status.FirstOrDefault(s => s.Code == code);
        if (item is null || item.Value.ValueKind != JsonValueKind.Number)
        {
            return null;
        }

        return item.Value.TryGetInt64(out var number) ? number : null;
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
