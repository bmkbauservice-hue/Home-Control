using HomeControl.Api.Auth;
using HomeControl.Api.Contracts;
using HomeControl.Api.Data;
using HomeControl.Api.GoogleHome;
using HomeControl.Api.Models;
using HomeControl.Api.Tuya;
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
        var devices = app.MapGroup("/api/devices").WithTags("Geräte");

        devices.MapGet("/", GetAllDevices);
        devices.MapGet("/{id:guid}", GetDeviceById);
        // Lesen ist offen (Frontend, n8n-Agent). Anlegen, Ändern, Löschen nur für Admin.
        devices.MapPost("/", CreateDevice).RequireAuthorization(AuthPolicies.AdminOnly);
        devices.MapPut("/{id:guid}", UpdateDevice).RequireAuthorization(AuthPolicies.AdminOnly);
        devices.MapDelete("/{id:guid}", DeleteDevice).RequireAuthorization(AuthPolicies.AdminOnly);
    }

    // GET /api/devices?q=...&roomId=...&type=... – alle Geräte, sortiert nach Raum und Name.
    // Alle Filter sind optional und werden mit UND kombiniert. Leer = keine Einschränkung.
    private static async Task<IResult> GetAllDevices(string? q, string? roomId, string? type, ApplicationDbContext db)
    {
        // roomId und type kommen als Text an, damit wir sie selbst prüfen
        // und eine Feldmeldung zurückgeben können.
        var errors = new Dictionary<string, string[]>();

        Guid? roomGuid = null;
        if (!string.IsNullOrWhiteSpace(roomId))
        {
            if (Guid.TryParse(roomId.Trim(), out var parsed))
            {
                roomGuid = parsed;
            }
            else
            {
                errors["roomId"] = [$"„{roomId}“ ist keine gültige Raum-Id."];
            }
        }

        var typeFilter = type?.Trim();
        if (!string.IsNullOrEmpty(typeFilter) && !DeviceTypes.Contains(typeFilter))
        {
            errors["type"] = [$"Den Gerätetyp „{typeFilter}“ gibt es nicht."];
        }

        if (errors.Count > 0)
        {
            return Results.ValidationProblem(errors);
        }

        var query = db.Devices.AsQueryable();

        // Eine gültige, aber unbekannte roomId liefert einfach eine leere Liste.
        if (roomGuid is not null)
        {
            query = query.Where(d => d.RoomId == roomGuid);
        }

        if (!string.IsNullOrEmpty(typeFilter))
        {
            query = query.Where(d => d.Type == typeFilter);
        }

        var search = q?.Trim();
        if (!string.IsNullOrEmpty(search))
        {
            var pattern = Search.ContainsPattern(search);
            query = query.Where(d => EF.Functions.Like(d.Name, pattern, Search.EscapeChar));
        }

        var devices = await query
            .OrderBy(d => d.Room.Name)
            .ThenBy(d => d.Name)
            .Select(d => new DeviceResponse(
                d.Id, d.Name, d.Type, d.RoomId, d.Room.Name, d.LastValue, d.TuyaDeviceId,
                d.PowerWatt, d.VoltageVolt, d.BatteryPercent, d.MeasuredAtUtc, d.TuyaSyncSeconds,
                d.PowerMeasuredAtUtc, d.EstimatedEnergyKwh, d.GoogleHomeDeviceId, d.BatteryReportedAtUtc))
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
    private static async Task<IResult> CreateDevice(DeviceRequest request, ApplicationDbContext db, GoogleHomeBridge bridge)
    {
        var errors = await Validate(request, null, null, null, db, bridge);
        if (errors.Count > 0)
        {
            return Results.ValidationProblem(errors);
        }

        var device = new Device
        {
            Name = request.Name.Trim(),
            Type = request.Type,
            RoomId = request.RoomId,
            TuyaDeviceId = NormalizeTuyaDeviceId(request.TuyaDeviceId),
            TuyaSyncSeconds = NormalizeTuyaSyncSeconds(request)
        };
        ApplyGoogleHomeLink(device, request, bridge);

        db.Devices.Add(device);
        await db.SaveChangesAsync();

        return Results.Created($"/api/devices/{device.Id}", await FindDevice(device.Id, db));
    }

    // PUT /api/devices/{id} – Name, Typ, Raum oder Tuya-Verknüpfung eines Geräts ändern
    private static async Task<IResult> UpdateDevice(Guid id, DeviceRequest request, ApplicationDbContext db, GoogleHomeBridge bridge)
    {
        var device = await db.Devices.FindAsync(id);
        if (device is null)
        {
            return DeviceNotFound(id);
        }

        // Der bisher gespeicherte Typ bleibt erlaubt, auch wenn er nicht mehr in der Liste steht.
        var errors = await Validate(request, device.Type, id, device.GoogleHomeDeviceId, db, bridge);
        if (errors.Count > 0)
        {
            return Results.ValidationProblem(errors);
        }

        // Wird die Tuya-Verknüpfung geändert oder entfernt, gehört der zuletzt von Tuya
        // gemeldete Zustand nicht mehr zu diesem Gerät. Er wird deshalb zurückgesetzt.
        var tuyaDeviceId = NormalizeTuyaDeviceId(request.TuyaDeviceId);
        if (tuyaDeviceId != device.TuyaDeviceId)
        {
            device.IsActive = false;
            device.LastValue = null;
            device.PowerWatt = null;
            device.PowerMeasuredAtUtc = null;
            device.EstimatedEnergyKwh = null;
            device.VoltageVolt = null;
            device.BatteryPercent = null;
            device.BatteryReportedAtUtc = null;
            device.MeasuredAtUtc = null;
        }

        device.Name = request.Name.Trim();
        device.Type = request.Type;
        device.RoomId = request.RoomId;
        device.TuyaDeviceId = tuyaDeviceId;
        device.TuyaSyncSeconds = NormalizeTuyaSyncSeconds(request);
        ApplyGoogleHomeLink(device, request, bridge);
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
            .Select(d => new DeviceResponse(
                d.Id, d.Name, d.Type, d.RoomId, d.Room.Name, d.LastValue, d.TuyaDeviceId,
                d.PowerWatt, d.VoltageVolt, d.BatteryPercent, d.MeasuredAtUtc, d.TuyaSyncSeconds,
                d.PowerMeasuredAtUtc, d.EstimatedEnergyKwh, d.GoogleHomeDeviceId, d.BatteryReportedAtUtc))
            .FirstOrDefaultAsync();
    }

    // Gleiche Grenze wie im ApplicationDbContext (TuyaDeviceId: HasMaxLength(64)).
    private const int MaxTuyaDeviceIdLength = 64;

    // Leer heißt: keine Google-Home-Verknüpfung.
    private static string? NormalizeGoogleHomeDeviceId(string? id) => string.IsNullOrWhiteSpace(id) ? null : id.Trim();

    // Bei einer neuen Verknüpfung gilt sofort der von Muju Home gemeldete Zustand, nicht der alte Tuya-Stand.
    private static void ApplyGoogleHomeLink(Device device, DeviceRequest request, GoogleHomeBridge bridge)
    {
        var googleId = NormalizeGoogleHomeDeviceId(request.GoogleHomeDeviceId);
        if (googleId != device.GoogleHomeDeviceId)
        {
            var isOn = googleId is null ? null : bridge.Find(googleId)?.On;
            device.LastValue = isOn is null ? null : isOn.Value ? TuyaSwitchRecorder.OnText : TuyaSwitchRecorder.OffText;
            device.IsActive = isOn == true;
        }
        device.GoogleHomeDeviceId = googleId;
    }

    // Erlaubte eigene Abgleich-Takte (Sekunden): 3, 5 oder 15 Minuten. null = Standard (30 Minuten).
    // Bewusst eine feste Auswahl statt jeder Zahl: Jeder Takt kostet Tuya-Kontingent (Rechnung in docs/TUYA.md).
    internal static readonly int[] AllowedTuyaSyncSeconds = [180, 300, 900];

    // Ohne Tuya-Verknüpfung gibt es nichts abzugleichen: dann immer null.
    internal static int? NormalizeTuyaSyncSeconds(DeviceRequest request) =>
        NormalizeTuyaDeviceId(request.TuyaDeviceId) is null ? null : request.TuyaSyncSeconds;

    // Leer oder nur Leerzeichen heißt: keine Tuya-Verknüpfung.
    internal static string? NormalizeTuyaDeviceId(string? tuyaDeviceId)
    {
        return string.IsNullOrWhiteSpace(tuyaDeviceId) ? null : tuyaDeviceId.Trim();
    }

    // Sammelt die Fehler pro Feld. Ein leeres Dictionary bedeutet: alles in Ordnung.
    // currentType ist beim Bearbeiten der gespeicherte Typ, beim Anlegen null.
    // currentDeviceId ist beim Bearbeiten die Id des Geräts selbst, beim Anlegen null.
    private static async Task<Dictionary<string, string[]>> Validate(
        DeviceRequest request, string? currentType, Guid? currentDeviceId, string? currentGoogleId,
        ApplicationDbContext db, GoogleHomeBridge bridge)
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

        var tuyaDeviceId = NormalizeTuyaDeviceId(request.TuyaDeviceId);
        if (tuyaDeviceId is not null)
        {
            // Tuya-Ids bestehen nur aus Buchstaben und Ziffern.
            if (tuyaDeviceId.Length > MaxTuyaDeviceIdLength || !tuyaDeviceId.All(char.IsAsciiLetterOrDigit))
            {
                errors["tuyaDeviceId"] = ["Das ist keine gültige Tuya-Geräte-Id."];
            }
            else
            {
                // Ein Tuya-Gerät darf nur mit einem Gerät verknüpft sein.
                var linkedName = await db.Devices
                    .Where(d => d.TuyaDeviceId == tuyaDeviceId && d.Id != currentDeviceId)
                    .Select(d => d.Name)
                    .FirstOrDefaultAsync();
                if (linkedName is not null)
                {
                    errors["tuyaDeviceId"] = [$"Dieses Tuya-Gerät ist schon mit „{linkedName}“ verknüpft."];
                }
            }
        }

        var googleId = NormalizeGoogleHomeDeviceId(request.GoogleHomeDeviceId);
        if (googleId is not null)
        {
            if (request.Type is not ("Light" or "Socket" or "Switch"))
            {
                errors["googleHomeDeviceId"] = ["Nur Licht, Steckdosen und Schalter lassen sich mit Google Home verknüpfen."];
            }
            else if (googleId.Length > GoogleHomeEndpoints.MaxDeviceIdLength)
            {
                errors["googleHomeDeviceId"] = ["Das ist keine gültige Google-Home-Geräte-Id."];
            }
            else
            {
                var linkedName = await db.Devices
                    .Where(d => d.GoogleHomeDeviceId == googleId && d.Id != currentDeviceId)
                    .Select(d => d.Name)
                    .FirstOrDefaultAsync();
                var reported = bridge.Find(googleId);
                if (linkedName is not null)
                {
                    errors["googleHomeDeviceId"] = [$"Dieses Google-Home-Gerät ist schon mit „{linkedName}“ verknüpft."];
                }
                else if (googleId != currentGoogleId && reported?.On is null)
                {
                    // Neu verknüpft wird nur, was Muju Home gerade wirklich mit Ein/Aus meldet.
                    errors["googleHomeDeviceId"] = ["Muju Home meldet dieses Gerät gerade nicht mit Ein/Aus."];
                }
                else if (reported is not null && GoogleHomeBridge.IsProtectedName(reported.Name))
                {
                    errors["googleHomeDeviceId"] = ["Dieses Gerät ist ein reiner Messpunkt und wird nie geschaltet."];
                }
            }
        }

        if (request.TuyaSyncSeconds is { } seconds && !AllowedTuyaSyncSeconds.Contains(seconds))
        {
            errors["tuyaSyncSeconds"] = ["Bitte einen Abgleich-Takt aus der Liste wählen (3, 5 oder 15 Minuten oder Standard)."];
        }

        return errors;
    }
}

