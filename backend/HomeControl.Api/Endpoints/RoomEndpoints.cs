using HomeControl.Api.Auth;
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
        // Lesen ist offen (Frontend, n8n-Agent). Anlegen, Ändern, Löschen nur für Admin.
        rooms.MapPost("/", CreateRoom).RequireAuthorization(AuthPolicies.AdminOnly);
        rooms.MapPut("/{id:guid}", UpdateRoom).RequireAuthorization(AuthPolicies.AdminOnly);
        rooms.MapDelete("/{id:guid}", DeleteRoom).RequireAuthorization(AuthPolicies.AdminOnly);
    }

    // GET /api/rooms?q=... – alle Räume, alphabetisch sortiert, optional mit Suche im Namen
    private static async Task<IResult> GetAllRooms(string? q, ApplicationDbContext db)
    {
        var query = db.Rooms.AsQueryable();

        // Leere Suche (oder nur Leerzeichen) bedeutet: keine Einschränkung.
        var search = q?.Trim();
        if (!string.IsNullOrEmpty(search))
        {
            var pattern = Search.ContainsPattern(search);
            query = query.Where(r => EF.Functions.Like(r.Name, pattern, Search.EscapeChar));
        }

        var rooms = await query
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

        return room is null ? RoomNotFound(id) : Results.Ok(room);
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
            return RoomNotFound(id);
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
            return RoomNotFound(id);
        }

        db.Rooms.Remove(room);
        await db.SaveChangesAsync();

        return Results.NoContent();
    }

    // 404 als ProblemDetails mit verständlicher Meldung.
    private static IResult RoomNotFound(Guid id)
    {
        return Results.Problem(
            statusCode: StatusCodes.Status404NotFound,
            title: "Raum nicht gefunden.",
            detail: $"Es gibt keinen Raum mit der Id {id}.");
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
