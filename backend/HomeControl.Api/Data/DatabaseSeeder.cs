using HomeControl.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace HomeControl.Api.Data;

// Legt Demo-Daten an, damit man die App ohne echte Geräte ausprobieren kann.
// Wird nur in Development und nur mit "SeedDemoData": true ausgeführt (siehe Program.cs).
// Es gibt keine echte Geräteverbindung: IsActive und LastValue bleiben auf ihren Standardwerten.
public class DatabaseSeeder(ApplicationDbContext db, ILogger<DatabaseSeeder> logger)
{
    public async Task SeedAsync()
    {
        // Nur in eine komplett leere Datenbank schreiben. Sobald irgendetwas existiert,
        // bleibt alles unverändert. So entstehen auch bei mehrfachem Start keine Duplikate.
        if (await db.Rooms.AnyAsync() || await db.Devices.AnyAsync() || await db.DeviceEvents.AnyAsync())
        {
            logger.LogInformation("Datenbank enthält bereits Daten – es werden keine Demo-Daten angelegt.");
            return;
        }

        var livingRoom = new Room { Name = "Wohnzimmer (Demo)" };
        var bedroom = new Room { Name = "Schlafzimmer (Demo)" };
        var entrance = new Room { Name = "Eingang (Demo)" };

        // Gerätetypen aus der erlaubten Liste in DeviceEndpoints.
        var lamp = new Device { Name = "Lampe (Demo)", Type = "Light", Room = livingRoom };
        var socket = new Device { Name = "Steckdose (Demo)", Type = "Socket", Room = bedroom };
        var temperatureSensor = new Device { Name = "Temperatursensor (Demo)", Type = "TemperatureSensor", Room = livingRoom };
        var doorSensor = new Device { Name = "Türsensor (Demo)", Type = "DoorSensor", Room = entrance };

        // Einige Ereignisse für den heutigen UTC-Tag, in dieser Reihenfolge.
        var events = new List<(Device Device, string Action, string? Value)>
        {
            (lamp, "turned_on", null),
            (doorSensor, "door_opened", null),
            (doorSensor, "door_closed", null),
            (temperatureSensor, "temperature_changed", "21,5 °C (Demo)"),
            (socket, "turned_on", null),
            (lamp, "turned_off", null),
            (socket, "turned_off", null)
        };

        // Die Zeitpunkte werden gleichmäßig zwischen 00:00 UTC und jetzt verteilt.
        // So liegen sie immer am heutigen UTC-Tag und nie in der Zukunft.
        var now = DateTimeOffset.UtcNow;
        var startOfDay = new DateTimeOffset(now.UtcDateTime.Date, TimeSpan.Zero);
        var step = (now - startOfDay) / (events.Count + 1);

        for (var i = 0; i < events.Count; i++)
        {
            db.DeviceEvents.Add(new DeviceEvent
            {
                Device = events[i].Device,
                Action = events[i].Action,
                Value = events[i].Value,
                Timestamp = startOfDay + step * (i + 1)
            });
        }

        // Räume und Geräte werden über die Verknüpfungen (Room, Device) automatisch mitgespeichert.
        // Ein einziges SaveChanges speichert alles gemeinsam in einer Transaktion.
        await db.SaveChangesAsync();

        logger.LogInformation("Demo-Daten angelegt: 3 Räume, 4 Geräte, {EventCount} Ereignisse.", events.Count);
    }
}
