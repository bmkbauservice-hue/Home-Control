using HomeControl.Api.Climate;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace HomeControl.Api.Tests;

// Prüft die echte SQLite-Struktur der separaten Klima-Datenbank.
public class ClimateDbContextTests
{
    [Fact]
    public async Task Sensoren_und_Raumwerte_werden_persistent_gespeichert()
    {
        await using var sqlite = new SqliteConnection("Data Source=:memory:");
        await sqlite.OpenAsync();
        var options = new DbContextOptionsBuilder<ClimateDbContext>()
            .UseSqlite(sqlite).Options;
        var room = Guid.NewGuid();
        var now = new DateTimeOffset(2026, 11, 6, 8, 30, 0, TimeSpan.Zero);

        await using (var db = new ClimateDbContext(options))
        {
            await db.PrepareAsync();
            db.Sensors.Add(new BridgeSensor
            {
                SensorId = "test-thermometer", Name = "Testgerät", RoomId = room,
                TemperatureCelsius = 21.5, HumidityPercent = 42.7, UpdatedAtUtc = now,
            });
            db.Readings.Add(new ClimateReading
            {
                RoomId = room, TemperatureCelsius = 21.5,
                HumidityPercent = 42.7, Source = "sensor", UpdatedAtUtc = now,
            });
            await db.SaveChangesAsync();
        }

        await using (var db = new ClimateDbContext(options))
        {
            await db.PrepareAsync(); // darf eine bestehende Datenbank nicht leeren
            var sensor = await db.Sensors.SingleAsync();
            var reading = await db.Readings.SingleAsync();

            Assert.Equal(room, sensor.RoomId);
            Assert.Equal(21.5, sensor.TemperatureCelsius);
            Assert.Equal(42.7, sensor.HumidityPercent);
            Assert.Equal(now, reading.UpdatedAtUtc);
            Assert.Equal("sensor", reading.Source);
        }
    }
}
