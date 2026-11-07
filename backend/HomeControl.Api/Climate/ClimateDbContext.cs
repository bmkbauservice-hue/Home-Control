using Microsoft.EntityFrameworkCore;

namespace HomeControl.Api.Climate;

// Eigene SQLite-Datei: Die vorhandene Hausdatenbank und ihre Migrationen bleiben unverändert.
// ClimateSensors entspricht dem Format der früheren Google-Home-Sensor-Bridge.
public sealed class ClimateDbContext(DbContextOptions<ClimateDbContext> options) : DbContext(options)
{
    public DbSet<BridgeSensor> Sensors => Set<BridgeSensor>();
    public DbSet<ClimateReading> Readings => Set<ClimateReading>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        builder.Entity<BridgeSensor>(entity =>
        {
            entity.ToTable("ClimateSensors");
            entity.HasKey(sensor => sensor.SensorId);
            entity.Property(sensor => sensor.SensorId).HasMaxLength(256);
            entity.Property(sensor => sensor.Name).HasMaxLength(120);
        });

        builder.Entity<ClimateReading>(entity =>
        {
            entity.ToTable("RoomClimate");
            entity.HasKey(reading => reading.RoomId);
        });
    }

    // Additiv, idempotent: Auch eine vorhandene Klima-Datenbank wird nicht gelöscht.
    // Die Hausdatenbank homecontrol.db wird davon nicht berührt.
    public async Task PrepareAsync(CancellationToken ct = default)
    {
        await Database.ExecuteSqlRawAsync("""
            CREATE TABLE IF NOT EXISTS ClimateSensors (
                SensorId TEXT NOT NULL PRIMARY KEY,
                Name TEXT NOT NULL,
                RoomId TEXT NULL,
                TemperatureCelsius REAL NULL,
                HumidityPercent REAL NULL,
                UpdatedAtUtc TEXT NOT NULL
            );
            """, ct);

        await Database.ExecuteSqlRawAsync("""
            CREATE TABLE IF NOT EXISTS RoomClimate (
                RoomId TEXT NOT NULL PRIMARY KEY,
                TemperatureCelsius REAL NULL,
                HumidityPercent REAL NULL,
                Source TEXT NOT NULL,
                UpdatedAtUtc TEXT NOT NULL
            );
            """, ct);
    }
}

public sealed class BridgeSensor
{
    public string SensorId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public Guid? RoomId { get; set; }
    public double? TemperatureCelsius { get; set; }
    public double? HumidityPercent { get; set; }
    public DateTimeOffset UpdatedAtUtc { get; set; }
}

public sealed class ClimateReading
{
    public Guid RoomId { get; set; }
    public double? TemperatureCelsius { get; set; }
    public double? HumidityPercent { get; set; }
    public string Source { get; set; } = "sensor";
    public DateTimeOffset UpdatedAtUtc { get; set; }
}
