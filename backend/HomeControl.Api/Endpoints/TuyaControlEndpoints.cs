using System.Text.Json;
using HomeControl.Api.Auth;
using HomeControl.Api.Data;
using HomeControl.Api.Demo;
using HomeControl.Api.RateLimiting;
using HomeControl.Api.Realtime;
using HomeControl.Api.Tuya;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace HomeControl.Api.Endpoints;

// Schaltet ausschließlich verknüpfte Tuya-Schaltgeräte, nie Sensoren oder Demo-Geräte.
public static class TuyaControlEndpoints
{
    public record SwitchRequest(bool? On);
    public record SwitchResponse(Guid DeviceId, bool On, string Message);

    public static void MapTuyaControlEndpoints(this WebApplication app)
    {
        app.MapPost("/api/tuya/devices/{id:guid}/switch", Switch)
            .WithTags("Tuya")
            .RequireAuthorization(AuthPolicies.AdminOnly)
            .RequireRateLimiting(RateLimitPolicies.Tuya);
    }

    private static async Task<IResult> Switch(
        Guid id, SwitchRequest request, ApplicationDbContext db, TuyaClient client,
        IOptionsMonitor<TuyaOptions> options, IConfiguration config,
        DeviceChangeHub changeHub, ILoggerFactory loggerFactory, CancellationToken cancellationToken)
    {
        if (DemoMode.IsEnabled(config))
            return Results.Problem(statusCode: 409, title: "Im Demo-Modus werden keine echten Geräte geschaltet.");

        if (request.On is not bool desired)
            return Results.Problem(statusCode: 400, title: "Bitte den gewünschten Zustand angeben.");

        var device = await db.Devices.FirstOrDefaultAsync(d => d.Id == id, cancellationToken);
        if (device is null)
            return DeviceEndpoints.DeviceNotFound(id);

        if (device.Type is not ("Light" or "Socket" or "Switch") || device.TuyaDeviceId is null)
            return Results.Problem(statusCode: 422, title: "Dieses Gerät lässt sich nicht über Tuya schalten.");

        if (options.CurrentValue.GetMissingCredentials().Count > 0)
            return Results.Problem(statusCode: 503, title: "Tuya ist noch nicht eingerichtet.");

        try
        {
            // Erst prüfen: Das Gerät muss in der Cloud online sein und switch_1 als Boolean melden.
            var cloudDevices = await client.GetLinkedAppDevicesAsync(cancellationToken);
            var online = cloudDevices.FirstOrDefault(d => d.Id == device.TuyaDeviceId)?.Online;
            if (online != true)
                return Results.Problem(statusCode: 409, title: "Gerät ist offline oder sein Online-Status ist unbekannt.");

            var before = await client.GetDeviceStatusAsync(device.TuyaDeviceId, cancellationToken);
            var oldValue = TuyaDeviceSyncer.GetBoolean(before, TuyaSwitchRecorder.SwitchCode);
            if (oldValue is null)
                return Results.Problem(statusCode: 422, title: "Das Gerät meldet keinen unterstützten Schaltcode switch_1.");

            if (oldValue == desired)
                return await SaveConfirmed(db, device, before, desired, changeHub, cancellationToken);

            // Ein Befehl, keine automatische Wiederholung: damit ein Klick nicht mehrfach schaltet.
            var accepted = await client.SendSwitchCommandAsync(
                device.TuyaDeviceId, TuyaSwitchRecorder.SwitchCode, desired, cancellationToken);
            if (!accepted)
                return Results.Problem(statusCode: 502, title: "Tuya hat den Schaltbefehl nicht angenommen.");

            // Die Cloud bestätigt zunächst nur die Annahme. Den neuen Zustand separat nachlesen.
            for (var attempt = 0; attempt < 3; attempt++)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(700), cancellationToken);
                var after = await client.GetDeviceStatusAsync(device.TuyaDeviceId, cancellationToken);
                if (TuyaDeviceSyncer.GetBoolean(after, TuyaSwitchRecorder.SwitchCode) == desired)
                    return await SaveConfirmed(db, device, after, desired, changeHub, cancellationToken);
            }

            return Results.Problem(statusCode: 409,
                title: "Befehl gesendet, aber der neue Gerätezustand wurde noch nicht bestätigt.");
        }
        catch (Exception ex) when (ex is TuyaApiException or HttpRequestException or JsonException
                                     || (ex is TaskCanceledException && !cancellationToken.IsCancellationRequested))
        {
            loggerFactory.CreateLogger("HomeControl.TuyaSteuerung").LogWarning(ex, "Tuya-Schaltaufruf fehlgeschlagen.");
            return Results.Problem(statusCode: 502, title: "Tuya-Steuerung derzeit nicht erreichbar.");
        }
    }

    private static async Task<IResult> SaveConfirmed(
        ApplicationDbContext db, HomeControl.Api.Models.Device device, List<TuyaStatusItem> status,
        bool desired, DeviceChangeHub changeHub, CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        TuyaDeviceSyncer.Apply(db, device, status, now, online: true);
        await db.SaveChangesAsync(cancellationToken);
        changeHub.Publish(new DeviceChangeEvent([device.Id], DeviceChangeSources.TuyaSync, now));
        return Results.Ok(new SwitchResponse(device.Id, desired, "Schaltzustand von Tuya bestätigt."));
    }
}
