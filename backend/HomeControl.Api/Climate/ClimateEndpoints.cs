using HomeControl.Api.Auth;
using HomeControl.Api.Data;
using HomeControl.Api.Demo;
using Microsoft.EntityFrameworkCore;

namespace HomeControl.Api.Climate;

// Google-Home-Sensoren, Raumzuordnung und die letzten echten Klimamesswerte.
public static class ClimateEndpoints
{
    private static readonly TimeSpan MaxFreshAge = TimeSpan.FromMinutes(20);

    public record SensorInput(string SensorId, string Name, double? TemperatureCelsius, double? HumidityPercent);
    public record AssignRoomInput(Guid? RoomId);
    public record RoomClimateResponse(
        Guid RoomId, double? TemperatureCelsius, double? HumidityPercent,
        DateTimeOffset UpdatedAtUtc, string Source, bool Stale);
    public record SensorResponse(
        string SensorId, string Name, Guid? RoomId, double? TemperatureCelsius,
        double? HumidityPercent, DateTimeOffset UpdatedAtUtc, bool Stale);

    public static void MapClimateEndpoints(this WebApplication app)
    {
        var climate = app.MapGroup("/api/climate").WithTags("Raumklima");
        climate.MapGet("", GetReadings).RequireAuthorization(AuthPolicies.SignedInUser);
        climate.MapGet("/bridge/sensors", GetSensors).RequireAuthorization(AuthPolicies.SignedInUser);
        climate.MapGet("/bridge/status", GetStatus).RequireAuthorization(AuthPolicies.SignedInUser);
        climate.MapPut("/bridge/sensors/{sensorId}/room", AssignRoom)
            .RequireAuthorization(AuthPolicies.AdminOnly);
        // Der lokale Sensor-Gateway benutzt einen separaten Schlüssel statt des Browser-JWT.
        climate.MapPost("/bridge/readings", Receive).AllowAnonymous();
    }

    private static bool IsStale(DateTimeOffset time) =>
        DateTimeOffset.UtcNow - time > MaxFreshAge || time > DateTimeOffset.UtcNow.AddMinutes(1);

    private static async Task<IResult> GetReadings(ClimateDbContext db, CancellationToken ct)
    {
        await db.PrepareAsync(ct);
        var readings = await db.Readings.AsNoTracking().ToListAsync(ct);
        return Results.Ok(readings.Select(reading => new RoomClimateResponse(
            reading.RoomId, reading.TemperatureCelsius, reading.HumidityPercent,
            reading.UpdatedAtUtc, reading.Source, IsStale(reading.UpdatedAtUtc))));
    }

    private static async Task<IResult> GetSensors(ClimateDbContext db, CancellationToken ct)
    {
        await db.PrepareAsync(ct);
        var sensors = await db.Sensors.AsNoTracking().OrderBy(sensor => sensor.Name).ToListAsync(ct);
        return Results.Ok(sensors.Select(sensor => new SensorResponse(
            sensor.SensorId, sensor.Name, sensor.RoomId, sensor.TemperatureCelsius,
            sensor.HumidityPercent, sensor.UpdatedAtUtc, IsStale(sensor.UpdatedAtUtc))));
    }

    private static async Task<IResult> GetStatus(ClimateDbContext db, IConfiguration config, CancellationToken ct)
    {
        await db.PrepareAsync(ct);
        var sensors = await db.Sensors.AsNoTracking().ToListAsync(ct);
        var latest = sensors.Count > 0
            ? (DateTimeOffset?)sensors.Max(sensor => sensor.UpdatedAtUtc)
            : null;
        return Results.Ok(new
        {
            bridgeConfigured = (config["SensorBridge:Key"]?.Length ?? 0) >= 32,
            sensors = sensors.Count,
            assigned = sensors.Count(sensor => sensor.RoomId is not null),
            lastReceivedAtUtc = latest,
            hasFreshReadings = latest is not null && !IsStale(latest.Value)
        });
    }

    private static async Task<IResult> Receive(
        HttpContext context, SensorInput input, ClimateDbContext db,
        IConfiguration config, CancellationToken ct)
    {
        if (DemoMode.IsEnabled(config))
            return Results.Problem(statusCode: 409, title: "In der Demo werden keine echten Sensorwerte angenommen.");

        if (SensorBridgeGuard.Reject(context, config) is { } rejected)
            return rejected;

        if (context.Request.ContentLength is > 4096)
            return Results.StatusCode(StatusCodes.Status413PayloadTooLarge);

        if (string.IsNullOrWhiteSpace(input.SensorId) || input.SensorId.Length > 256 ||
            string.IsNullOrWhiteSpace(input.Name) || input.Name.Length > 120 ||
            (input.TemperatureCelsius is null && input.HumidityPercent is null) ||
            (input.TemperatureCelsius is { } temp && (!double.IsFinite(temp) || temp is < -40 or > 60)) ||
            (input.HumidityPercent is { } hum && (!double.IsFinite(hum) || hum is < 0 or > 100)))
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["sensor"] = ["Sensor-ID, Name oder Messwert ist ungültig."]
            });

        await db.PrepareAsync(ct);
        var sensorId = input.SensorId.Trim();
        var sensor = await db.Sensors.FirstOrDefaultAsync(s => s.SensorId == sensorId, ct);
        if (sensor is null)
        {
            sensor = new BridgeSensor { SensorId = sensorId };
            db.Sensors.Add(sensor);
        }
        sensor.Name = input.Name.Trim();
        // Fehlt ein Wert im aktuellen Paket, wird er nicht als frisch ausgegeben.
        sensor.TemperatureCelsius = input.TemperatureCelsius;
        sensor.HumidityPercent = input.HumidityPercent;
        sensor.UpdatedAtUtc = DateTimeOffset.UtcNow;
        if (sensor.RoomId is { } roomId)
            await UpdateRoom(db, roomId, sensor, ct);

        await db.SaveChangesAsync(ct);
        return Results.Ok(new { received = true, assigned = sensor.RoomId is not null });
    }

    private static async Task<IResult> AssignRoom(
        string sensorId, AssignRoomInput input, ApplicationDbContext house, ClimateDbContext db,
        CancellationToken ct)
    {
        if (input.RoomId is { } roomId &&
            !await house.Rooms.AsNoTracking().AnyAsync(room => room.Id == roomId, ct))
            return Results.NotFound(new { title = "Raum existiert nicht." });

        await db.PrepareAsync(ct);
        var sensor = await db.Sensors.FirstOrDefaultAsync(s => s.SensorId == sensorId, ct);
        if (sensor is null)
            return Results.NotFound(new { title = "Sensor hat noch keine Werte übertragen." });

        var previousRoom = sensor.RoomId;
        sensor.RoomId = input.RoomId;

        if (input.RoomId is { } nextRoom)
            await UpdateRoom(db, nextRoom, sensor, ct);

        if (previousRoom is { } oldRoom && previousRoom != input.RoomId)
        {
            // Ein anderer Sensor im alten Raum darf seine Daten behalten.
            var hasOthers = await db.Sensors.AsNoTracking()
                .AnyAsync(other => other.SensorId != sensorId && other.RoomId == oldRoom, ct);
            if (!hasOthers)
            {
                var previousReading = await db.Readings.FirstOrDefaultAsync(r => r.RoomId == oldRoom, ct);
                if (previousReading?.Source == "sensor")
                    db.Readings.Remove(previousReading);
            }
        }

        await db.SaveChangesAsync(ct);
        return Results.Ok(new { sensor.SensorId, sensor.RoomId });
    }

    private static async Task UpdateRoom(ClimateDbContext db, Guid roomId, BridgeSensor sensor,
        CancellationToken ct)
    {
        var reading = await db.Readings.FirstOrDefaultAsync(r => r.RoomId == roomId, ct);
        if (reading is null)
        {
            reading = new ClimateReading { RoomId = roomId };
            db.Readings.Add(reading);
        }
        reading.TemperatureCelsius = sensor.TemperatureCelsius;
        reading.HumidityPercent = sensor.HumidityPercent;
        reading.UpdatedAtUtc = sensor.UpdatedAtUtc;
        reading.Source = "sensor";
    }
}
