using HomeControl.Api.Contracts;
using HomeControl.Api.Data;
using HomeControl.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace HomeControl.Api.Endpoints;

public static class RoomEndpoints
{
    private const int MaxNameLength = 100;

    // Registriert alle Endpunkte unter /api/rooms.
    public static void MapRoomEndpoints(this WebApplication app)
    {
        var rooms = app.MapGroup("/api/rooms");

        rooms.MapGet("/", GetAllRooms);
        rooms.MapGet("/{id:guid}", GetRoomById);
        rooms.MapPost("/", CreateRoom);
        rooms.MapPut("/{id:guid}", UpdateRoom);
        rooms.MapDelete("/{id:guid}", DeleteRoom);
    }

    // GET /api/rooms – alle Räume, alphabetisch sortiert
    private static async Task<IResult> GetAllRooms(ApplicationDbContext db)
    {
        var rooms = await db.Rooms
            .OrderBy(r => r.Name)
            .Select(r => new RoomResponse(r.Id, r.Name, r.Devices.Count))
            .ToListAsync();

        return Results.Ok(rooms);
    }

    // GET /api/rooms/{id} – ein einzelner Raum
    private static async Task<IResult> GetRoomById(Guid id, ApplicationDbContext db)
    {
        var room = await db.Rooms
            .Where(r => r.Id == id)
            .Select(r => new RoomResponse(r.Id, r.Name, r.Devices.Count))
            .FirstOrDefaultAsync();

        return room is null ? Results.NotFound() : Results.Ok(room);
    }

    // POST /api/rooms – neuen Raum anlegen
    private static async Task<IResult> CreateRoom(RoomRequest request, ApplicationDbContext db)
    {
        var error = ValidateName(request.Name);
        if (error is not null)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["name"] = [error]
            });
        }

        var room = new Room { Name = request.Name.Trim() };

        db.Rooms.Add(room);
        await db.SaveChangesAsync();

        var response = new RoomResponse(room.Id, room.Name, 0);
        return Results.Created($"/api/rooms/{room.Id}", response);
    }

    // PUT /api/rooms/{id} – Namen eines Raums ändern
    private static async Task<IResult> UpdateRoom(Guid id, RoomRequest request, ApplicationDbContext db)
    {
        var error = ValidateName(request.Name);
        if (error is not null)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["name"] = [error]
            });
        }

        var room = await db.Rooms.FindAsync(id);
        if (room is null)
        {
            return Results.NotFound();
        }

        room.Name = request.Name.Trim();
        await db.SaveChangesAsync();

        var deviceCount = await db.Devices.CountAsync(d => d.RoomId == id);
        return Results.Ok(new RoomResponse(room.Id, room.Name, deviceCount));
    }

    // DELETE /api/rooms/{id} – Raum löschen (Geräte werden per Cascade mitgelöscht)
    private static async Task<IResult> DeleteRoom(Guid id, ApplicationDbContext db)
    {
        var room = await db.Rooms.FindAsync(id);
        if (room is null)
        {
            return Results.NotFound();
        }

        db.Rooms.Remove(room);
        await db.SaveChangesAsync();

        return Results.NoContent();
    }

    // Gibt eine Fehlermeldung zurück oder null, wenn der Name in Ordnung ist.
    // Wird auch für Gerätenamen genutzt (DeviceEndpoints).
    internal static string? ValidateName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return "Bitte einen Namen eingeben.";
        }

        if (name.Trim().Length > MaxNameLength)
        {
            return $"Der Name darf höchstens {MaxNameLength} Zeichen lang sein.";
        }

        return null;
    }
}
