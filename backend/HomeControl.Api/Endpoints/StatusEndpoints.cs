using System.Text.Json;
using HomeControl.Api.Contracts;
using HomeControl.Api.Data;
using HomeControl.Api.GoogleHome;
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
        GoogleHomeBridge googleHome,
        IOptionsMonitor<TuyaOptions> options,
        TimeProvider timeProvider,
        ILogger<TuyaOnlineCache> logger,
        CancellationToken cancellationToken)
    {
        var devices = await db.Devices
            .OrderBy(d => d.Name)
            .Select(d => new DeviceLink(d.Id, d.Name, d.TuyaDeviceId, d.GoogleHomeDeviceId))
            .ToListAsync(cancellationToken);

        string tuya;
        IReadOnlyDictionary<string, bool?>? cloudOnline = null;
        DateTimeOffset? tuyaCheckedAtUtc = null;

        if (options.CurrentValue.GetMissingCredentials().Count > 0)
        {
            tuya = OnlineStates.NotConfigured;
        }
        else if (cache.BackgroundRefreshActive)
        {
            // Der Hintergrund-Abgleich hält den Speicher aktuell: nur lesen, Tuya NICHT selbst fragen.
            // So kosten die LEDs kein Kontingent, auch wenn ein Tablet die App den ganzen Tag offen hat.
            // Dafür ist der Stand so alt wie der letzte Durchlauf (höchstens Tuya:AutoSyncSeconds).
            var latest = cache.Latest;
            if (latest is null)
            {
                tuya = OnlineStates.Unknown;   // Der erste Durchlauf läuft noch.
            }
            else if (latest.LastAttemptFailed)
            {
                tuya = OnlineStates.Offline;
            }
            else
            {
                tuya = OnlineStates.Online;
                cloudOnline = latest.Online;
                tuyaCheckedAtUtc = latest.LoadedAtUtc;
            }
        }
        else
        {
            // Rückfallebene ohne Hintergrund-Abgleich: selbst fragen, höchstens alle 5 Minuten (TuyaOnlineCache.MaxAge).
            try
            {
                cloudOnline = await cache.GetAsync(client.GetLinkedAppDevicesAsync, cancellationToken);
                tuya = OnlineStates.Online;
                tuyaCheckedAtUtc = cache.Latest?.LoadedAtUtc;
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

        var states = DeviceOnlineEvaluator.Evaluate(devices, cloudOnline, googleHome.OnlineSnapshot());

        return Results.Ok(new StatusResponse(
            timeProvider.GetUtcNow(),
            tuya,
            states.Count(s => s.State == OnlineStates.Online),
            states.Count(s => s.State == OnlineStates.Offline),
            states.Count(s => s.State == OnlineStates.Unknown),
            states,
            tuyaCheckedAtUtc));
    }
}
