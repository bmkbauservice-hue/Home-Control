using System.Globalization;
using HomeControl.Api.Data;
using HomeControl.Api.Models;
using HomeControl.Api.Tuya;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace HomeControl.Api.Demo;

// Füllt die Demo-Datenbank (homecontrol-demo.db) mit dem Beispielhaus und den Ereignissen der letzten 7 Tage,
// damit Übersicht und Reports sofort nach "echtem Zuhause" aussehen. Läuft nur im Demo-Modus (Program.cs).
//
// - Haus und Ereignisse: nur in eine leere Datenbank, sonst bleibt alles, wie es ist (kein doppeltes Anlegen).
// - Demo-Konto: bei JEDEM Start angelegt bzw. auf das Passwort aus demo-starten.sh zurückgesetzt, damit
//   das angezeigte Passwort immer stimmt.
// Die Ereignisse entstehen mit einem festen Startwert für Random: bei jedem neuen Anlegen dieselben.
public class DemoDataSeeder(
    ApplicationDbContext db,
    UserManager<User> userManager,
    RoleManager<IdentityRole<Guid>> roleManager,
    TimeProvider timeProvider,
    ILogger<DemoDataSeeder> logger)
{
    private static readonly TimeZoneInfo Berlin = TimeZoneInfo.FindSystemTimeZoneById("Europe/Berlin");

    public const int HistoryDays = 7;

    public async Task SeedAsync(string password, CancellationToken cancellationToken = default)
    {
        await EnsureDemoUserAsync(password);

        if (await db.Rooms.AnyAsync(cancellationToken) || await db.Devices.AnyAsync(cancellationToken))
        {
            logger.LogInformation("Demo: Beispielhaus ist schon angelegt.");
            return;
        }

        var now = timeProvider.GetUtcNow();
        var rooms = DemoHouse.Rooms.ToDictionary(name => name, name => new Room { Name = name });
        var devices = DemoHouse.Devices.ToDictionary(d => d.Key, d => new Device
        {
            Name = d.Name,
            Type = d.Type,
            Room = rooms[d.Room],
            // Erfundene Tuya-Id: Der Hintergrund-Abgleich fragt die Fake-Cloud (DemoTuyaHandler) nach diesem Gerät.
            TuyaDeviceId = d.TuyaId,
            TuyaSyncSeconds = d.SyncSeconds,
        });

        var events = CreateHistory(now)
            .OrderBy(e => e.Timestamp)
            .Select(e => new DeviceEvent
            {
                Device = devices[e.Key],
                Action = e.Action,
                Value = e.Value,
                Timestamp = DateTimeOffset.FromUnixTimeMilliseconds(e.Timestamp.ToUnixTimeMilliseconds()),
            })
            .ToList();

        // Letzter Zustand jedes Geräts passend zum letzten Ereignis (der Abgleich aktualisiert ihn danach live).
        foreach (var e in events)
        {
            ApplyToDevice(e.Device, e.Action, e.Value);
        }

        // Räume und Geräte ausdrücklich hinzufügen: Ein Gerät ohne Ereignis (z. B. der Kühlschrank, immer an)
        // wäre sonst über keine Verknüpfung erreichbar und würde nicht gespeichert.
        db.Rooms.AddRange(rooms.Values);
        db.Devices.AddRange(devices.Values);
        db.DeviceEvents.AddRange(events);
        await db.SaveChangesAsync(cancellationToken);
        logger.LogInformation("Demo: Beispielhaus angelegt ({Rooms} Räume, {Devices} Geräte, {Events} Ereignisse).",
            rooms.Count, devices.Count, events.Count);
    }

    // Ereignisse der letzten 7 Tage bis kurz vor jetzt, nach einem Tagesablauf mit etwas Zufall.
    public static List<(string Key, string Action, string? Value, DateTimeOffset Timestamp)> CreateHistory(DateTimeOffset nowUtc)
    {
        var result = new List<(string, string, string?, DateTimeOffset)>();
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(nowUtc, Berlin).DateTime);
        var random = new Random(2026);
        var end = nowUtc.AddMinutes(-5);

        for (var offset = HistoryDays - 1; offset >= 0; offset--)
        {
            var day = today.AddDays(-offset);
            var weekday = day.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday);

            // Eine Zeitspanne "an" als zwei Ereignisse. Minuten mit etwas Zufall, damit nicht jeder Tag gleich ist.
            void OnOff(string key, int fromHour, int fromMinute, int minutes)
            {
                var on = At(day, fromHour, fromMinute + random.Next(-20, 21));
                result.Add((key, "turned_on", null, on));
                result.Add((key, "turned_off", null, on.AddMinutes(minutes + random.Next(-15, 16))));
            }

            OnOff("deckenlicht", 6, 40, 90);
            OnOff("deckenlicht", 18, 0, 160);
            OnOff("leselampe", 19, 0, 210);
            OnOff("nachttisch", 22, 0, 60);
            OnOff("fernseher", 19, 30, 180);
            OnOff("kaffee", 7, 0, 5);
            if (random.Next(2) == 0) OnOff("kaffee", 15, 0, 4);
            if (weekday)
            {
                OnOff("schreibtisch", 8, 15, 510);
                OnOff("computer", 8, 20, 500);
            }

            // Drucker: bis vor zwei Tagen benutzt, seitdem ausgesteckt (offline).
            if (offset >= 2) OnOff("drucker", 10, 0, 30);

            // Haustür: 5 bis 9 Mal am Tag kurz offen.
            for (int i = 0, count = random.Next(5, 10); i < count; i++)
            {
                var open = At(day, 7 + random.Next(0, 15), random.Next(0, 60));
                result.Add(("haustuer", "door_opened", null, open));
                result.Add(("haustuer", "door_closed", null, open.AddMinutes(random.Next(1, 5))));
            }

            // Temperatur viermal am Tag, Wohnzimmer wärmer als Schlafzimmer.
            foreach (var hour in new[] { 6, 11, 16, 21 })
            {
                result.Add(("temp-wohnen", "temperature_changed", Celsius(20.5 + random.NextDouble() * 2), At(day, hour, random.Next(0, 60))));
                result.Add(("temp-schlafen", "temperature_changed", Celsius(18 + random.NextDouble() * 2), At(day, hour, random.Next(0, 60))));
            }
        }

        // Nichts in der Zukunft: Ereignisse von heute nur bis kurz vor jetzt.
        return result.Where(e => e.Item4 <= end).ToList();
    }

    // "21,4 °C" wie in der Oberfläche
    private static string Celsius(double value) =>
        value.ToString("0.0", CultureInfo.GetCultureInfo("de-DE")) + " °C";

    // Uhrzeit in Berlin → UTC. minute darf über 59 oder unter 0 gehen (wird umgerechnet).
    private static DateTimeOffset At(DateOnly day, int hour, int minute)
    {
        var local = day.ToDateTime(TimeOnly.MinValue).AddHours(hour).AddMinutes(minute);
        return new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(local, Berlin), TimeSpan.Zero);
    }

    // Gleiche Texte wie beim Tuya-Abgleich (EIN/AUS/OFFEN/ZU), Temperatur als Text.
    private static void ApplyToDevice(Device device, string action, string? value)
    {
        switch (action)
        {
            case "turned_on":
                device.IsActive = true;
                device.LastValue = TuyaSwitchRecorder.OnText;
                break;
            case "turned_off":
                device.IsActive = false;
                device.LastValue = TuyaSwitchRecorder.OffText;
                break;
            case "door_opened":
                device.LastValue = TuyaDeviceSyncer.OpenText;
                break;
            case "door_closed":
                device.LastValue = TuyaDeviceSyncer.ClosedText;
                break;
            case "temperature_changed":
                device.LastValue = value;
                break;
        }
    }

    // Demo-Konto als Admin, damit in der Vorführung alles geht (Anlegen, Bearbeiten, "Neues Bild").
    private async Task EnsureDemoUserAsync(string password)
    {
        foreach (var role in new[] { Roles.Admin, Roles.User })
        {
            if (!await roleManager.RoleExistsAsync(role))
            {
                await roleManager.CreateAsync(new IdentityRole<Guid>(role));
            }
        }

        var user = await userManager.FindByEmailAsync(DemoHouse.Email);
        if (user is null)
        {
            user = new User
            {
                Id = Guid.NewGuid(),
                UserName = DemoHouse.Email,
                Email = DemoHouse.Email,
                CreatedAtUtc = timeProvider.GetUtcNow(),
            };
            Check(await userManager.CreateAsync(user, password));
            Check(await userManager.AddToRoleAsync(user, Roles.Admin));
            logger.LogInformation("Demo: Konto {Email} angelegt.", DemoHouse.Email);
            return;
        }

        // Schon da: Passwort auf den Wert aus dem Skript setzen und eine Sperre (5 Fehlversuche) aufheben.
        Check(await userManager.RemovePasswordAsync(user));
        Check(await userManager.AddPasswordAsync(user, password));
        await userManager.SetLockoutEndDateAsync(user, null);
        await userManager.ResetAccessFailedCountAsync(user);
    }

    private static void Check(IdentityResult result)
    {
        if (!result.Succeeded)
        {
            throw new InvalidOperationException(
                "Demo-Konto konnte nicht eingerichtet werden: " + string.Join("; ", result.Errors.Select(e => e.Description)));
        }
    }
}
