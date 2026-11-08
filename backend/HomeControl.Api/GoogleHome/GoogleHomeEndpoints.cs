using HomeControl.Api.Auth;
using HomeControl.Api.Climate;
using HomeControl.Api.Data;
using HomeControl.Api.Demo;
using HomeControl.Api.Endpoints;
using HomeControl.Api.Models;
using HomeControl.Api.RateLimiting;
using HomeControl.Api.Realtime;
using HomeControl.Api.Tuya;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;

namespace HomeControl.Api.GoogleHome;

// Schalten über Muju Home und Google Home. Nur Admin, nur ausdrücklich verknüpfte Geräte,
// und "Ein" oder "Aus" erst, wenn das Gerät den Zustand bestätigt hat. Siehe docs/GOOGLE_HOME_SCHALTEN.md.
public static class GoogleHomeEndpoints
{
    public record BridgeDeviceInput(string Id, string Name, bool Online, bool? On);
    public record PollInput(List<BridgeDeviceInput>? Devices);
    public record CommandResultInput(bool? On, string? Error);
    public record SwitchRequest(bool? On);
    public record SwitchResponse(Guid DeviceId, bool On, string Message);
    public record ReportedDevice(string Id, string Name, bool Online, bool? On, bool Protected);
    public record DevicesResponse(bool Connected, List<ReportedDevice> Devices);

    private const long MaxBodyBytes = 64 * 1024;
    private const int MaxDevices = 200;
    internal const int MaxDeviceIdLength = 256;
    private const int MaxNameLength = 120;

    public static void MapGoogleHomeEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/google-home").WithTags("Google Home");
        group.MapGet("/devices", GetDevices).RequireAuthorization(AuthPolicies.AdminOnly);
        group.MapPost("/devices/{id:guid}/switch", Switch)
            .RequireAuthorization(AuthPolicies.AdminOnly)
            .RequireRateLimiting(RateLimitPolicies.Tuya);

        // Die App benutzt denselben Schlüssel wie die Klima-Bridge statt eines Browser-JWT.
        group.MapPost("/bridge/poll", Poll).AllowAnonymous();
        group.MapPost("/bridge/commands/{commandId:guid}/result", ReportResult).AllowAnonymous();
        // Nur für das Diagnosewerkzeug auf diesem Rechner. Der Gateway leitet diese Adresse nicht weiter.
        group.MapGet("/bridge/devices", GetDevicesForTool).AllowAnonymous();
    }

    private static IResult GetDevices(GoogleHomeBridge bridge) =>
        Results.Ok(new DevicesResponse(bridge.IsConnected, bridge.Devices
            .Select(d => new ReportedDevice(d.Id, d.Name, d.Online, d.On, GoogleHomeBridge.IsProtectedName(d.Name)))
            .ToList()));

    private static IResult GetDevicesForTool(HttpContext context, GoogleHomeBridge bridge, IConfiguration config) =>
        RejectBridgeCall(context, config) ?? GetDevices(bridge);

    private static async Task<IResult> Poll(
        HttpContext context, PollInput input, GoogleHomeBridge bridge, ApplicationDbContext db,
        IConfiguration config, DeviceChangeHub changeHub, CancellationToken cancellationToken)
    {
        if (RejectBridgeCall(context, config) is { } rejected) return rejected;

        var reported = input.Devices ?? [];
        if (reported.Count > MaxDevices || reported.Any(IsInvalid) ||
            reported.Select(d => d.Id).Distinct().Count() != reported.Count)
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["devices"] = ["Geräteliste ist ungültig."]
            });

        var commands = bridge.Poll(reported.Select(d => new BridgeDevice(d.Id.Trim(), d.Name.Trim(), d.Online, d.On)));
        await ApplyReportedStates(bridge, db, changeHub, cancellationToken);
        return Results.Ok(new { commands });
    }

    private static bool IsInvalid(BridgeDeviceInput device) =>
        string.IsNullOrWhiteSpace(device.Id) || device.Id.Length > MaxDeviceIdLength ||
        string.IsNullOrWhiteSpace(device.Name) || device.Name.Length > MaxNameLength;

    private static IResult ReportResult(
        HttpContext context, Guid commandId, CommandResultInput input, GoogleHomeBridge bridge, IConfiguration config)
    {
        if (RejectBridgeCall(context, config) is { } rejected) return rejected;

        // Eine verspätete Antwort gehört zu keinem wartenden Befehl mehr.
        return bridge.Complete(commandId, input.On, input.Error)
            ? Results.Ok(new { accepted = true })
            : Results.NotFound(new { title = "Dieser Befehl wird nicht mehr erwartet." });
    }

    private static IResult? RejectBridgeCall(HttpContext context, IConfiguration config)
    {
        if (DemoMode.IsEnabled(config))
            return Results.Problem(statusCode: 409, title: "In der Demo gibt es keine Google-Home-Anbindung.");
        if (SensorBridgeGuard.Reject(context, config) is { } rejected)
            return rejected;
        return context.Request.ContentLength is > MaxBodyBytes
            ? Results.StatusCode(StatusCodes.Status413PayloadTooLarge)
            : null;
    }

    // Änderungen, die nicht aus Home Control kommen (z. B. über die Google Home-App), sichtbar machen.
    private static async Task ApplyReportedStates(
        GoogleHomeBridge bridge, ApplicationDbContext db, DeviceChangeHub changeHub, CancellationToken cancellationToken)
    {
        var linked = await db.Devices.Where(d => d.GoogleHomeDeviceId != null).ToListAsync(cancellationToken);
        var now = DateTimeOffset.UtcNow;
        var changed = new List<Guid>();
        foreach (var device in linked)
        {
            // Solange ein Befehl läuft, entscheidet allein dessen Bestätigung.
            if (bridge.HasPending(device.GoogleHomeDeviceId!)) continue;
            if (bridge.Find(device.GoogleHomeDeviceId!) is { Online: true, On: { } isOn } && SetState(db, device, isOn, now))
                changed.Add(device.Id);
        }

        if (changed.Count == 0) return;
        await db.SaveChangesAsync(cancellationToken);
        changeHub.Publish(new DeviceChangeEvent(changed, DeviceChangeSources.GoogleHome, now));
    }

    private static bool SetState(ApplicationDbContext db, Device device, bool isOn, DateTimeOffset now)
    {
        var value = isOn ? TuyaSwitchRecorder.OnText : TuyaSwitchRecorder.OffText;
        if (device.LastValue == value && device.IsActive == isOn) return false;

        device.LastValue = value;
        device.IsActive = isOn;
        db.DeviceEvents.Add(new DeviceEvent
        {
            DeviceId = device.Id,
            Action = isOn ? "turned_on" : "turned_off",
            Timestamp = DateTimeOffset.FromUnixTimeMilliseconds(now.ToUnixTimeMilliseconds()),
        });
        return true;
    }

    private static async Task<IResult> Switch(
        Guid id, SwitchRequest request, ApplicationDbContext db, GoogleHomeBridge bridge,
        IConfiguration config, DeviceChangeHub changeHub, CancellationToken cancellationToken)
    {
        if (DemoMode.IsEnabled(config))
            return Results.Problem(statusCode: 409, title: "Im Demo-Modus werden keine echten Geräte geschaltet.");
        if (request.On is not bool desired)
            return Results.Problem(statusCode: 400, title: "Bitte den gewünschten Zustand angeben.");

        var device = await db.Devices.FirstOrDefaultAsync(d => d.Id == id, cancellationToken);
        if (device is null)
            return DeviceEndpoints.DeviceNotFound(id);
        if (device.Type is not ("Light" or "Socket" or "Switch") || device.GoogleHomeDeviceId is null)
            return Results.Problem(statusCode: 422, title: "Dieses Gerät ist nicht mit Google Home verknüpft.");

        // Der Zustand kommt allein aus Google Home. Ein alter Tuya-Status spielt hier keine Rolle.
        if (!bridge.IsConnected)
            return Results.Problem(statusCode: 503, title: "Muju Home ist nicht verbunden. Bitte App und USB-Verbindung am Handy prüfen. Nichts wurde geschaltet.");

        var reported = bridge.Find(device.GoogleHomeDeviceId);
        if (reported is null)
            return Results.Problem(statusCode: 409, title: "Muju Home meldet dieses Gerät gerade nicht. Nichts wurde geschaltet.");
        if (GoogleHomeBridge.IsProtectedName(reported.Name))
            return Results.Problem(statusCode: 422, title: "Dieses Gerät ist ein reiner Messpunkt und wird nie geschaltet.");
        if (!reported.Online)
            return Results.Problem(statusCode: 409, title: "Gerät ist laut Google Home offline. Nichts wurde geschaltet.");
        if (reported.On is null)
            return Results.Problem(statusCode: 422, title: "Google Home meldet für dieses Gerät keinen Ein/Aus-Zustand.");

        // Hat das Gerät den gewünschten Zustand schon gemeldet, braucht es keinen Befehl.
        if (reported.On == desired)
            return await SaveConfirmed(db, device, desired, changeHub, cancellationToken);

        return await bridge.SendAsync(device.GoogleHomeDeviceId, desired, cancellationToken) switch
        {
            CommandOutcome.Confirmed => await SaveConfirmed(db, device, desired, changeHub, cancellationToken),
            CommandOutcome.DeviceOffline => Results.Problem(statusCode: 409, title: "Gerät ist laut Google Home offline. Nichts wurde geschaltet."),
            CommandOutcome.DeviceUnknown => Results.Problem(statusCode: 409, title: "Muju Home kennt dieses Gerät nicht. Nichts wurde geschaltet."),
            CommandOutcome.Rejected => Results.Problem(statusCode: 502, title: "Google Home hat den Schaltbefehl abgelehnt."),
            CommandOutcome.NotPickedUp => Results.Problem(statusCode: 504, title: "Muju Home hat den Befehl nicht rechtzeitig abgeholt. Nichts wurde geschaltet."),
            CommandOutcome.Busy => Results.Problem(statusCode: 409, title: "Für dieses Gerät läuft gerade schon ein Schaltbefehl."),
            CommandOutcome.NoAnswer => Results.Problem(statusCode: 504, title: "Keine Rückmeldung von Muju Home. Der Zustand ist unbekannt, bitte an der Lampe prüfen."),
            _ => Results.Problem(statusCode: 409, title: "Befehl gesendet, aber der neue Gerätezustand wurde nicht bestätigt."),
        };
    }

    private static async Task<IResult> SaveConfirmed(
        ApplicationDbContext db, Device device, bool desired, DeviceChangeHub changeHub, CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        SetState(db, device, desired, now);
        await db.SaveChangesAsync(cancellationToken);
        changeHub.Publish(new DeviceChangeEvent([device.Id], DeviceChangeSources.GoogleHome, now));
        return Results.Ok(new SwitchResponse(device.Id, desired, "Schaltzustand vom Gerät bestätigt."));
    }
}
