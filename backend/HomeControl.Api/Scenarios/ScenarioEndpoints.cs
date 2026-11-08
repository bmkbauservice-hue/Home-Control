using System.Text.Json;
using HomeControl.Api.Auth;
using HomeControl.Api.Blink;
using HomeControl.Api.Data;
using HomeControl.Api.Endpoints;
using HomeControl.Api.Models;
using HomeControl.Api.RateLimiting;
using Microsoft.EntityFrameworkCore;

namespace HomeControl.Api.Scenarios;

// Regeln anlegen, ändern und ein- oder ausschalten. Alles nur für Admins. Siehe docs/SZENARIEN.md.
public static class ScenarioEndpoints
{
    public record ScenarioInput(string Name, Guid TriggerDeviceId, string BlinkSystemId);
    public record EnabledInput(bool? Enabled);
    public record RunResponse(DateTimeOffset StartedAtUtc, DateTimeOffset? FinishedAtUtc, string Trigger, string Outcome, string Message);
    public record ScenarioResponse(Guid Id, string Name, bool Enabled, Guid TriggerDeviceId, string TriggerDeviceName,
        string BlinkSystemId, string BlinkSystemName, List<RunResponse> Runs);
    public record DoorOption(Guid Id, string Name, string RoomName, bool HasFreshSignal);
    public record SystemOption(string Id, string Name, bool? Armed);
    public record OptionsResponse(List<DoorOption> Doors, List<SystemOption> Systems, string? SystemsError);

    private const int MaxNameLength = 100;

    public static void MapScenarioEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/scenarios").WithTags("Szenarien").RequireAuthorization(AuthPolicies.AdminOnly);
        group.MapGet("", GetAll);
        group.MapGet("/options", GetOptions);
        group.MapPost("", Create);
        group.MapPut("/{id:guid}", Update);
        group.MapPut("/{id:guid}/enabled", SetEnabled);
        group.MapDelete("/{id:guid}", Delete);
        // Echter Test: schaltet Blink wirklich scharf. Höchstens einmal pro Minute und Regel.
        group.MapPost("/{id:guid}/run", RunOnce).RequireRateLimiting(RateLimitPolicies.BlinkSnapshot);
    }

    private static async Task<IResult> GetAll(ApplicationDbContext db, CancellationToken ct)
    {
        var scenarios = await db.Scenarios.AsNoTracking().Include(s => s.Runs).Include(s => s.TriggerDevice).ToListAsync(ct);
        return Results.Ok(scenarios.OrderBy(s => s.Name, StringComparer.CurrentCulture).Select(ToResponse));
    }

    private static ScenarioResponse ToResponse(Scenario s) => new(
        s.Id, s.Name, s.Enabled, s.TriggerDeviceId, s.TriggerDevice.Name, s.BlinkSystemId, s.BlinkSystemName,
        s.Runs.OrderByDescending(r => r.StartedAtUtc)
            .Select(r => new RunResponse(r.StartedAtUtc, r.FinishedAtUtc, r.Trigger, r.Outcome, r.Message)).ToList());

    private static async Task<IResult> GetOptions(
        ApplicationDbContext db, IBlinkCameraClient blink, ILogger<ScenarioQueue> logger, CancellationToken ct)
    {
        var doors = await db.Devices.AsNoTracking().Where(d => d.Type == "DoorSensor")
            .Select(d => new DoorOption(d.Id, d.Name, d.Room.Name, d.GoogleHomeDeviceId != null)).ToListAsync(ct);
        var (systems, error) = await LoadSystemsAsync(blink, logger, ct);
        return Results.Ok(new OptionsResponse(doors, systems ?? [], error));
    }

    private static async Task<(List<SystemOption>? Systems, string? Error)> LoadSystemsAsync(
        IBlinkCameraClient blink, ILogger logger, CancellationToken ct)
    {
        try
        {
            var systems = await blink.GetSystemsAsync(ct);
            return (systems.Select(s => new SystemOption(s.Id, s.Name, s.Armed)).ToList(), null);
        }
        catch (Exception ex) when (ex is BlinkApiException or BlinkLoginRequiredException or BlinkNotConfiguredException
                                     or BlinkDisabledException or HttpRequestException or JsonException
                                     || (ex is TaskCanceledException && !ct.IsCancellationRequested))
        {
            logger.LogWarning("Blink-Systeme nicht abrufbar ({ExceptionType}).", ex.GetType().Name);
            return (null, "Blink-Systeme sind gerade nicht abrufbar.");
        }
    }

    private static async Task<IResult> Create(
        ScenarioInput input, ApplicationDbContext db, IBlinkCameraClient blink, TimeProvider time,
        ILogger<ScenarioQueue> logger, CancellationToken ct)
    {
        var (errors, systemName) = await ValidateAsync(input, db, blink, logger, ct);
        if (errors.Count > 0) return Results.ValidationProblem(errors);

        // Eine neue Regel löst nie aus, bevor der Admin sie ausdrücklich einschaltet.
        var scenario = new Scenario
        {
            Name = input.Name.Trim(),
            Enabled = false,
            TriggerDeviceId = input.TriggerDeviceId,
            BlinkSystemId = input.BlinkSystemId,
            BlinkSystemName = systemName!,
            CreatedAtUtc = time.GetUtcNow(),
        };
        db.Scenarios.Add(scenario);
        await db.SaveChangesAsync(ct);
        return Results.Created($"/api/scenarios/{scenario.Id}", await LoadResponseAsync(db, scenario.Id, ct));
    }

    private static async Task<IResult> Update(
        Guid id, ScenarioInput input, ApplicationDbContext db, IBlinkCameraClient blink,
        ILogger<ScenarioQueue> logger, CancellationToken ct)
    {
        var scenario = await db.Scenarios.FirstOrDefaultAsync(s => s.Id == id, ct);
        if (scenario is null) return NotFound();

        var (errors, systemName) = await ValidateAsync(input, db, blink, logger, ct);
        if (errors.Count > 0) return Results.ValidationProblem(errors);

        scenario.Name = input.Name.Trim();
        scenario.TriggerDeviceId = input.TriggerDeviceId;
        scenario.BlinkSystemId = input.BlinkSystemId;
        scenario.BlinkSystemName = systemName!;
        await db.SaveChangesAsync(ct);
        return Results.Ok(await LoadResponseAsync(db, id, ct));
    }

    private static async Task<(Dictionary<string, string[]> Errors, string? SystemName)> ValidateAsync(
        ScenarioInput input, ApplicationDbContext db, IBlinkCameraClient blink, ILogger logger, CancellationToken ct)
    {
        var errors = new Dictionary<string, string[]>();
        if (string.IsNullOrWhiteSpace(input.Name) || input.Name.Trim().Length > MaxNameLength)
            errors["name"] = ["Bitte einen Namen mit höchstens 100 Zeichen eingeben."];

        var door = await db.Devices.AsNoTracking().FirstOrDefaultAsync(d => d.Id == input.TriggerDeviceId, ct);
        if (door is null || door.Type != "DoorSensor")
            errors["triggerDeviceId"] = ["Bitte einen vorhandenen Türsensor wählen."];
        else if (door.GoogleHomeDeviceId is null)
            errors["triggerDeviceId"] = ["Dieser Türsensor ist nicht mit Google Home verknüpft. Ohne das gibt es kein frisches Türsignal."];

        var (systems, error) = await LoadSystemsAsync(blink, logger, ct);
        var system = systems?.FirstOrDefault(s => s.Id == input.BlinkSystemId);
        if (system is null)
            errors["blinkSystemId"] = [error ?? "Bitte ein vorhandenes Blink-System wählen."];
        return (errors, system?.Name);
    }

    private static async Task<ScenarioResponse> LoadResponseAsync(ApplicationDbContext db, Guid id, CancellationToken ct) =>
        ToResponse(await db.Scenarios.AsNoTracking().Include(s => s.Runs).Include(s => s.TriggerDevice).FirstAsync(s => s.Id == id, ct));

    private static async Task<IResult> SetEnabled(Guid id, EnabledInput input, ApplicationDbContext db, CancellationToken ct)
    {
        if (input.Enabled is not bool enabled)
            return Results.Problem(statusCode: 400, title: "Bitte den gewünschten Zustand angeben.");

        var scenario = await db.Scenarios.FirstOrDefaultAsync(s => s.Id == id, ct);
        if (scenario is null) return NotFound();

        scenario.Enabled = enabled;
        await db.SaveChangesAsync(ct);
        return Results.Ok(await LoadResponseAsync(db, id, ct));
    }

    private static async Task<IResult> Delete(Guid id, ApplicationDbContext db, CancellationToken ct)
    {
        var scenario = await db.Scenarios.FirstOrDefaultAsync(s => s.Id == id, ct);
        if (scenario is null) return NotFound();

        db.Scenarios.Remove(scenario);
        await db.SaveChangesAsync(ct);
        return Results.NoContent();
    }

    private static async Task<IResult> RunOnce(Guid id, ApplicationDbContext db, ScenarioQueue queue, TimeProvider time, CancellationToken ct)
    {
        if (!await db.Scenarios.AnyAsync(s => s.Id == id, ct)) return NotFound();

        queue.Enqueue(new ScenarioTrigger(null, id, time.GetUtcNow()));
        return Results.Accepted();
    }

    private static IResult NotFound() =>
        Results.Problem(statusCode: StatusCodes.Status404NotFound, title: "Diese Regel gibt es nicht.");
}
