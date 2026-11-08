using HomeControl.Api.Auth;
using HomeControl.Api.Blink;
using HomeControl.Api.Data;
using HomeControl.Api.Endpoints;
using HomeControl.Api.RateLimiting;
using Microsoft.EntityFrameworkCore;

namespace HomeControl.Api.Alarm;

// Alarmseite: Türereignisse aus dem Szenarienverlauf und Clips vom USB-Stick. Nur für Admins. Siehe docs/ALARM.md.
public static class AlarmEndpoints
{
    public record AlarmEvent(Guid Id, DateTimeOffset StartedAtUtc, DateTimeOffset? FinishedAtUtc, string Trigger,
        string Outcome, string Message, string ScenarioName, string DoorName, string BlinkSystemName);
    public record AlarmClip(string Key, string CameraName, DateTimeOffset CreatedAtUtc);
    public record AlarmClipsResponse(List<AlarmClip> Clips, string? Problem);

    private const int MaxEvents = 50;

    public static void MapAlarmEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/alarm").WithTags("Alarm").RequireAuthorization(AuthPolicies.AdminOnly);
        group.MapGet("/events", GetEvents);
        group.MapGet("/clips", GetClips);
        // Der Clip läuft über das Backend, damit der Browser weder Blink-Adressen noch Token sieht.
        group.MapGet("/clips/{key:regex(^l[A-Za-z0-9]+$)}/video", GetVideo).RequireRateLimiting(RateLimitPolicies.BlinkThumbnails);
    }

    // Nur der vorhandene Szenarienverlauf: Ein Ereignis erscheint auch dann, wenn es keinen Clip dazu gibt.
    private static async Task<IResult> GetEvents(ApplicationDbContext db, CancellationToken ct)
    {
        var runs = await db.ScenarioRuns.AsNoTracking().Include(r => r.Scenario).ThenInclude(s => s.TriggerDevice).ToListAsync(ct);
        return Results.Ok(runs.OrderByDescending(r => r.StartedAtUtc).Take(MaxEvents)
            .Select(r => new AlarmEvent(r.Id, r.StartedAtUtc, r.FinishedAtUtc, r.Trigger, r.Outcome, r.Message,
                r.Scenario.Name, r.Scenario.TriggerDevice.Name, r.Scenario.BlinkSystemName)));
    }

    private static async Task<IResult> GetClips(
        bool? refresh, IBlinkCameraClient blink, ILogger<IBlinkCameraClient> logger, CancellationToken ct)
    {
        try
        {
            var list = await blink.GetClipsAsync(refresh == true, ct);
            return Results.Ok(new AlarmClipsResponse(
                list.Clips.Select(c => new AlarmClip(c.Key, c.CameraName, c.CreatedAtUtc)).ToList(), list.Problem));
        }
        catch (Exception ex) when (IsNotConnected(ex))
        {
            return NotConnected(ex);
        }
        catch (Exception ex) when (BlinkEndpoints.TryMapTransportError(ex, logger, ct, out var problem))
        {
            return problem!;
        }
    }

    private static async Task<IResult> GetVideo(
        string key, IBlinkCameraClient blink, HttpContext http, ILogger<IBlinkCameraClient> logger, CancellationToken ct)
    {
        try
        {
            var video = await blink.GetClipVideoAsync(key, ct);
            if (video is null)
                return Results.Problem(statusCode: StatusCodes.Status404NotFound, title: "Diesen Clip gibt es nicht mehr.");

            // no-store: Kameravideos nicht im Browser-Cache oder in einem Proxy ablegen.
            http.Response.Headers.CacheControl = "no-store";
            var playable = await ClipRepair.RemoveEmptyPacketsAsync(video.Content, logger, ct);
            return Results.File(playable, "video/mp4", enableRangeProcessing: true);
        }
        catch (Exception ex) when (IsNotConnected(ex))
        {
            return NotConnected(ex);
        }
        catch (Exception ex) when (BlinkEndpoints.TryMapTransportError(ex, logger, ct, out var problem))
        {
            return problem!;
        }
    }

    private static bool IsNotConnected(Exception ex) =>
        ex is BlinkDisabledException or BlinkNotConfiguredException or BlinkLoginRequiredException;

    private static IResult NotConnected(Exception ex) =>
        Results.Problem(statusCode: StatusCodes.Status503ServiceUnavailable, title: "Blink ist nicht verbunden.", detail: ex.Message);
}
