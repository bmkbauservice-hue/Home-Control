using System.Text.Json;
using HomeControl.Api.Contracts;
using HomeControl.Api.Data;
using HomeControl.Api.Status;
using HomeControl.Api.Tuya;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace HomeControl.Api.Endpoints;

public static class StatusEndpoints
{
    // Registriert GET /api/status. Nur lesend: Es wird nichts gespeichert und nichts geschaltet.
    public static void MapStatusEndpoints(this WebApplication app)
    {
        app.MapGet("/api/status", GetStatus)
            .WithTags("Status")
            .WithSummary("Status-LEDs: Tuya-Cloud erreichbar? Welche Geräte sind online?");
    }

    // GET /api/status – Übersicht für die Status-LEDs: Ist die Tuya-Cloud erreichbar, und welche
    // Geräte sind online? Antwortet immer mit 200, auch wenn Tuya nicht erreichbar ist: Das ist
    // dann eben eine rote LED und kein Fehler dieses Endpunkts.
    private static async Task<IResult> GetStatus(
        ApplicationDbContext db,
        TuyaClient client,
        TuyaOnlineCache cache,
        IOptionsMonitor<TuyaOptions> options,
        TimeProvider timeProvider,
        ILogger<TuyaOnlineCache> logger,
        CancellationToken cancellationToken)
    {
        var devices = await db.Devices
            .OrderBy(d => d.Name)
            .Select(d => new DeviceLink(d.Id, d.Name, d.TuyaDeviceId))
            .ToListAsync(cancellationToken);

        string tuya;
        IReadOnlyDictionary<string, bool?>? cloudOnline = null;

        if (options.CurrentValue.GetMissingCredentials().Count > 0)
        {
            tuya = OnlineStates.NotConfigured;
        }
        else
        {
            try
            {
                cloudOnline = await cache.GetAsync(client.GetLinkedAppDevicesAsync, cancellationToken);
                tuya = OnlineStates.Online;
            }
            catch (Exception ex) when (ex is TuyaApiException or HttpRequestException or JsonException
                || (ex is TaskCanceledException && !cancellationToken.IsCancellationRequested))
            {
                // Dieselben Fehler wie bei den Tuya-Endpunkten. Ins Log kommt nur Typ und Meldung.
                logger.LogWarning("Tuya-Status für die LEDs nicht abrufbar ({ExceptionType}): {Message}",
                    ex.GetType().Name, ex.Message);
                tuya = OnlineStates.Offline;
            }
        }

        var states = DeviceOnlineEvaluator.Evaluate(devices, cloudOnline);

        return Results.Ok(new StatusResponse(
            timeProvider.GetUtcNow(),
            tuya,
            states.Count(s => s.State == OnlineStates.Online),
            states.Count(s => s.State == OnlineStates.Offline),
            states.Count(s => s.State == OnlineStates.Unknown),
            states));
    }
}
