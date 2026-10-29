using HomeControl.Api.Auth;
using HomeControl.Api.Contracts;
using HomeControl.Api.Data;
using HomeControl.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace HomeControl.Api.Endpoints;

public static class DeviceEventEndpoints
{
    // Erlaubte Aktionen. Weitere können hier ergänzt werden.
    // Die deutschen Bezeichnungen stehen im Frontend (api/deviceEvents.ts).
    private static readonly string[] Actions =
        ["turned_on", "turned_off", "temperature_changed", "door_opened", "door_closed"];

    // Gleiche Grenze wie im ApplicationDbContext (Value: HasMaxLength(200)).
    private const int MaxValueLength = 200;

    // Registriert die Endpunkte unter /api/devices/{id}/events.
    public static void MapDeviceEventEndpoints(this WebApplication app)
    {
        var events = app.MapGroup("/api/devices/{id:guid}/events");

        events.MapGet("/", GetEvents);
        events.MapGet("/{eventId:guid}", GetEventById);
        // Manuelle Testeinträge nur für Admin.
        events.MapPost("/", CreateEvent).RequireAuthorization(AuthPolicies.AdminOnly);
    }

    // GET /api/devices/{id}/events – Verlauf eines Geräts, neueste Ereignisse zuerst
    private static async Task<IResult> GetEvents(Guid id, ApplicationDbContext db)
    {
        if (!await db.Devices.AnyAsync(d => d.Id == id))
        {
            return DeviceEndpoints.DeviceNotFound(id);
        }

        var events = await db.DeviceEvents
            .Where(e => e.DeviceId == id)
            .OrderByDescending(e => e.Timestamp)
            .Select(e => new DeviceEventResponse(e.Id, e.Timestamp, e.Action, e.Value))
            .ToListAsync();

        return Results.Ok(events);
    }

    // GET /api/devices/{id}/events/{eventId} – ein einzelnes Ereignis eines Geräts.
    // Auf diese Adresse verweist auch der Location-Header nach dem Anlegen (POST).
    private static async Task<IResult> GetEventById(Guid id, Guid eventId, ApplicationDbContext db)
    {
        if (!await db.Devices.AnyAsync(d => d.Id == id))
        {
            return DeviceEndpoints.DeviceNotFound(id);
        }

        // Das Ereignis muss zu genau diesem Gerät gehören. Ein Ereignis eines anderen
        // Geräts wird hier also nicht gefunden.
        var deviceEvent = await db.DeviceEvents
            .Where(e => e.Id == eventId && e.DeviceId == id)
            .Select(e => new DeviceEventResponse(e.Id, e.Timestamp, e.Action, e.Value))
            .FirstOrDefaultAsync();

        if (deviceEvent is null)
        {
            return Results.Problem(
                statusCode: StatusCodes.Status404NotFound,
                title: "Ereignis nicht gefunden.",
                detail: $"Das Gerät {id} hat kein Ereignis mit der Id {eventId}.");
        }

        return Results.Ok(deviceEvent);
    }

    // POST /api/devices/{id}/events – ein Ereignis speichern.
    // Es wird nur der Eintrag gespeichert. IsActive und LastValue des Geräts bleiben unverändert.
    // Antwort: 201 mit Location-Header auf GET /api/devices/{id}/events/{eventId}.
    private static async Task<IResult> CreateEvent(Guid id, DeviceEventRequest request, ApplicationDbContext db)
    {
        if (!await db.Devices.AnyAsync(d => d.Id == id))
        {
            return DeviceEndpoints.DeviceNotFound(id);
        }

        var errors = Validate(request);
        if (errors.Count > 0)
        {
            return Results.ValidationProblem(errors);
        }

        // Ein leerer Wert wird als "kein Wert" (null) gespeichert.
        var value = string.IsNullOrWhiteSpace(request.Value) ? null : request.Value.Trim();

        // Zeitpunkt in UTC, gekürzt auf Millisekunden. Genauer speichert die Datenbank nicht
        // (siehe HasConversion im DbContext). So liefert POST denselben Wert wie später GET.
        var now = DateTimeOffset.FromUnixTimeMilliseconds(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());

        var deviceEvent = new DeviceEvent
        {
            DeviceId = id,
            Action = request.Action,
            Value = value,
            Timestamp = now
        };

        db.DeviceEvents.Add(deviceEvent);
        await db.SaveChangesAsync();

        var response = new DeviceEventResponse(deviceEvent.Id, deviceEvent.Timestamp, deviceEvent.Action, deviceEvent.Value);
        return Results.Created($"/api/devices/{id}/events/{deviceEvent.Id}", response);
    }

    // Sammelt die Fehler pro Feld. Ein leeres Dictionary bedeutet: alles in Ordnung.
    private static Dictionary<string, string[]> Validate(DeviceEventRequest request)
    {
        var errors = new Dictionary<string, string[]>();

        if (string.IsNullOrWhiteSpace(request.Action))
        {
            errors["action"] = ["Bitte eine Aktion auswählen."];
        }
        else if (!Actions.Contains(request.Action))
        {
            errors["action"] = [$"Die Aktion „{request.Action}“ ist nicht erlaubt."];
        }

        if (request.Value is not null && request.Value.Trim().Length > MaxValueLength)
        {
            errors["value"] = [$"Der Wert darf höchstens {MaxValueLength} Zeichen lang sein."];
        }

        return errors;
    }
}
