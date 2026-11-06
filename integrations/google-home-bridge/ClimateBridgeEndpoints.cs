using System.Security.Cryptography;
using System.Text;
using HomeControl.Api.Auth;
using HomeControl.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace HomeControl.Api.Climate;

public static class ClimateBridgeEndpoints
{
    private const string Sql = """
        CREATE TABLE IF NOT EXISTS ClimateSensors (
          SensorId TEXT NOT NULL PRIMARY KEY,
          Name TEXT NOT NULL,
          RoomId TEXT NULL,
          TemperatureCelsius REAL NULL,
          HumidityPercent REAL NULL,
          UpdatedAtUtc TEXT NOT NULL
        );
        """;

    public static void MapClimateBridgeEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/climate/bridge").WithTags("Google Home Sensoren");
        group.MapPost("/readings", Receive).AllowAnonymous();
        group.MapGet("/sensors", ListSensors).RequireAuthorization(AuthPolicies.SignedInUser);
        group.MapPut("/sensors/{sensorId}/room", AssignRoom).RequireAuthorization(AuthPolicies.AdminOnly);
    }

    private static Task Init(ClimateDbContext db, CancellationToken ct) =>
        db.Database.ExecuteSqlRawAsync(Sql, cancellationToken: ct);

    private static async Task<IResult> Receive(
        HttpContext context, SensorInput input, ClimateDbContext db,
        IConfiguration config, CancellationToken ct)
    {
        var key = config["SensorBridge:Key"];
        if (string.IsNullOrEmpty(key) || key.Length < 32)
            return Results.Problem("Sensor-Bridge ist nicht konfiguriert.", statusCode: 503);

        var actual = Encoding.UTF8.GetBytes(context.Request.Headers["X-Sensor-Key"].ToString());
        var expected = Encoding.UTF8.GetBytes(key);
        if (actual.Length != expected.Length || !CryptographicOperations.FixedTimeEquals(actual, expected))
            return Results.Unauthorized();

        if (string.IsNullOrWhiteSpace(input.SensorId) || input.SensorId.Length > 256 ||
            string.IsNullOrWhiteSpace(input.Name) || input.Name.Length > 120 ||
            (input.TemperatureCelsius is null && input.HumidityPercent is null) ||
            (input.TemperatureCelsius is { } t && (!double.IsFinite(t) || t is < -40 or > 60)) ||
            (input.HumidityPercent is { } h && (!double.IsFinite(h) || h is < 0 or > 100)))
            return Results.BadRequest(new { title = "Ungültige Sensorwerte." });

        await Init(db, ct);
        var sensorId = input.SensorId.Trim();
        var sensor = await db.Set<BridgeSensor>().FirstOrDefaultAsync(s => s.SensorId == sensorId, ct);
        if (sensor is null)
        {
            sensor = new BridgeSensor { SensorId = sensorId };
            db.Set<BridgeSensor>().Add(sensor);
        }
        sensor.Name = input.Name.Trim();
        if (input.TemperatureCelsius is not null) sensor.TemperatureCelsius = input.TemperatureCelsius;
        if (input.HumidityPercent is not null) sensor.HumidityPercent = input.HumidityPercent;
        sensor.UpdatedAtUtc = DateTimeOffset.UtcNow;

        if (sensor.RoomId is { } roomId)
            await UpdateReading(db, roomId, input.TemperatureCelsius, input.HumidityPercent,
                sensor.UpdatedAtUtc, ct);

        await db.SaveChangesAsync(ct);
        return Results.Ok(new { received = true, assigned = sensor.RoomId is not null });
    }

    private static async Task<IResult> ListSensors(ClimateDbContext db, CancellationToken ct)
    {
        await Init(db, ct);
        var sensors = await db.Set<BridgeSensor>().AsNoTracking().OrderBy(s => s.Name).ToListAsync(ct);
        return Results.Ok(sensors.Select(s => new
        {
            s.SensorId, s.Name, s.RoomId, s.TemperatureCelsius,
            s.HumidityPercent, s.UpdatedAtUtc
        }));
    }

    private static async Task<IResult> AssignRoom(
        string sensorId, AssignRoomInput input,
        ApplicationDbContext homeDb, ClimateDbContext db, CancellationToken ct)
    {
        if (input.RoomId is { } roomId &&
            !await homeDb.Rooms.AsNoTracking().AnyAsync(r => r.Id == roomId, ct))
            return Results.NotFound(new { title = "Raum existiert nicht." });

        await Init(db, ct);
        var sensor = await db.Set<BridgeSensor>().FirstOrDefaultAsync(s => s.SensorId == sensorId, ct);
        if (sensor is null) return Results.NotFound(new { title = "Sensor noch nicht empfangen." });

        sensor.RoomId = input.RoomId;
        if (input.RoomId is { } assignedId)
            await UpdateReading(db, assignedId, sensor.TemperatureCelsius,
                sensor.HumidityPercent, sensor.UpdatedAtUtc, ct);
        await db.SaveChangesAsync(ct);
        return Results.Ok(new { sensor.SensorId, sensor.RoomId });
    }

    private static async Task UpdateReading(
        ClimateDbContext db, Guid roomId, double? temp, double? humidity,
        DateTimeOffset receivedAt, CancellationToken ct)
    {
        if (temp is null && humidity is null) return;
        var reading = await db.Readings.FirstOrDefaultAsync(r => r.RoomId == roomId, ct);
        if (reading is null)
        {
            reading = new ClimateReading { RoomId = roomId };
            db.Readings.Add(reading);
        }
        if (temp is not null) reading.TemperatureCelsius = temp;
        if (humidity is not null) reading.HumidityPercent = humidity;
        reading.Source = "sensor";
        reading.UpdatedAtUtc = receivedAt;
    }
}

public sealed record SensorInput(string SensorId, string Name, double? TemperatureCelsius, double? HumidityPercent);
public sealed record AssignRoomInput(Guid? RoomId);

public sealed class BridgeSensor
{
    public string SensorId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public Guid? RoomId { get; set; }
    public double? TemperatureCelsius { get; set; }
    public double? HumidityPercent { get; set; }
    public DateTimeOffset UpdatedAtUtc { get; set; }
}
