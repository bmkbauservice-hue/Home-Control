using HomeControl.Api.Contracts;
using HomeControl.Api.Data;
using HomeControl.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace HomeControl.Api.Endpoints;

public static class DeviceEndpoints
{
    // Erlaubte Gerätetypen für neue Geräte. Weitere Typen können hier ergänzt werden.
    // Die deutschen Bezeichnungen stehen im Frontend (api/devices.ts).
    private static readonly string[] DeviceTypes = ["Light", "Socket", "TemperatureSensor", "DoorSensor", "Switch"];

    // Registriert alle Endpunkte unter /api/devices.
    public static void MapDeviceEndpoints(this WebApplication app)
    {
        var devices = app.MapGroup("/api/devices");

        devices.MapGet("/", GetAllDevices);
        devices.MapGet("/{id:guid}", GetDeviceById);
        devices.MapPost("/", CreateDevice);
        devices.MapPut("/{id:guid}", UpdateDevice);
        devices.MapDelete("/{id:guid}", DeleteDevice);
    }

    // GET /api/devices – alle Geräte, sortiert nach Raum und Name
    private static async Task<IResult> GetAllDevices(ApplicationDbContext db)
    {
        var devices = await db.Devices
            .OrderBy(d => d.Room.Name)
            .ThenBy(d => d.Name)
            .Select(d => new DeviceResponse(d.Id, d.Name, d.Type, d.RoomId, d.Room.Name, d.LastValue))
            .ToListAsync();

        return Results.Ok(devices);
    }

    // GET /api/devices/{id} – ein einzelnes Gerät
    private static async Task<IResult> GetDeviceById(Guid id, ApplicationDbContext db)
    {
        var device = await FindDevice(id, db);
        return device is null ? DeviceNotFound(id) : Results.Ok(device);
    }

    // POST /api/devices – neues Gerät in einem Raum anlegen
    private static async Task<IResult> CreateDevice(DeviceRequest request, ApplicationDbContext db)
    {
        var errors = await Validate(request, null, db);
        if (errors.Count > 0)
        {
            return Results.ValidationProblem(errors);
        }

        var device = new Device
        {
            Name = request.Name.Trim(),
            Type = request.Type,
            RoomId = request.RoomId
        };

        db.Devices.Add(device);
        await db.SaveChangesAsync();

        return Results.Created($"/api/devices/{device.Id}", await FindDevice(device.Id, db));
    }

    // PUT /api/devices/{id} – Name, Typ oder Raum eines Geräts ändern
    private static async Task<IResult> UpdateDevice(Guid id, DeviceRequest request, ApplicationDbContext db)
    {
        var device = await db.Devices.FindAsync(id);
        if (device is null)
        {
            return DeviceNotFound(id);
        }

        // Der bisher gespeicherte Typ bleibt erlaubt, auch wenn er nicht mehr in der Liste steht.
        var errors = await Validate(request, device.Type, db);
        if (errors.Count > 0)
        {
            return Results.ValidationProblem(errors);
        }

        device.Name = request.Name.Trim();
        device.Type = request.Type;
        device.RoomId = request.RoomId;
        await db.SaveChangesAsync();

        return Results.Ok(await FindDevice(id, db));
    }

    // DELETE /api/devices/{id} – Gerät löschen (Ereignisse werden per Cascade mitgelöscht)
    private static async Task<IResult> DeleteDevice(Guid id, ApplicationDbContext db)
    {
        var device = await db.Devices.FindAsync(id);
        if (device is null)
        {
            return DeviceNotFound(id);
        }

        db.Devices.Remove(device);
        await db.SaveChangesAsync();

        return Results.NoContent();
    }

    // 404 als ProblemDetails mit verständlicher Meldung.
    // Wird auch von den Ereignis-Endpunkten genutzt (DeviceEventEndpoints).
    internal static IResult DeviceNotFound(Guid id)
    {
        return Results.Problem(
            statusCode: StatusCodes.Status404NotFound,
            title: "Gerät nicht gefunden.",
            detail: $"Es gibt kein Gerät mit der Id {id}.");
    }

    // Lädt ein Gerät samt Raumnamen so, wie die API es zurückgibt.
    private static Task<DeviceResponse?> FindDevice(Guid id, ApplicationDbContext db)
    {
        return db.Devices
            .Where(d => d.Id == id)
            .Select(d => new DeviceResponse(d.Id, d.Name, d.Type, d.RoomId, d.Room.Name, d.LastValue))
            .FirstOrDefaultAsync();
    }

    // Sammelt die Fehler pro Feld. Ein leeres Dictionary bedeutet: alles in Ordnung.
    // currentType ist beim Bearbeiten der gespeicherte Typ, beim Anlegen null.
    private static async Task<Dictionary<string, string[]>> Validate(
        DeviceRequest request, string? currentType, ApplicationDbContext db)
    {
        var errors = new Dictionary<string, string[]>();

        var nameError = RoomEndpoints.ValidateName(request.Name);
        if (nameError is not null)
        {
            errors["name"] = [nameError];
        }

        // Erlaubt: ein Typ aus der Liste oder der unveränderte, bereits gespeicherte Typ.
        var typeAllowed = DeviceTypes.Contains(request.Type)
            || (currentType is not null && request.Type == currentType);
        if (!typeAllowed)
        {
            errors["type"] = ["Bitte einen Gerätetyp auswählen."];
        }

        if (!await db.Rooms.AnyAsync(r => r.Id == request.RoomId))
        {
            errors["roomId"] = ["Bitte einen vorhandenen Raum auswählen."];
        }

        return errors;
    }
}
