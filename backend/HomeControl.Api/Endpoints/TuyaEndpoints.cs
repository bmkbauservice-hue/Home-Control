using System.Text.Json;
using HomeControl.Api.Contracts;
using HomeControl.Api.Data;
using HomeControl.Api.Tuya;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace HomeControl.Api.Endpoints;

public static class TuyaEndpoints
{
    // Hinweis, der jeder Statusantwort beiliegt.
    private const string StatusNote =
        "Werte, die die Tuya-Cloud zum Abrufzeitpunkt für das Gerät kennt. " +
        "Ein erfolgreicher Abruf bedeutet nicht, dass das Gerät online ist.";

    // Registriert die Endpunkte unter /api/tuya. Es wird nie ein Gerät geschaltet.
    // GET liest nur. POST /sync liest ebenfalls nur bei Tuya, speichert den Zustand aber
    // zusätzlich in unserer Datenbank (siehe TuyaSwitchRecorder).
    public static void MapTuyaEndpoints(this WebApplication app)
    {
        var tuya = app.MapGroup("/api/tuya");

        tuya.MapGet("/devices", GetCloudDevices);
        tuya.MapGet("/test-device/status", GetTestDeviceStatus);
        // Schreibt in die Datenbank (Gerätezustand), deshalb nur für angemeldete Benutzer. Die
        // Dashboard-Karte ruft ihn nach dem Frontend-Login auf. Kein Admin nötig: Es werden keine
        // Eingaben angenommen, nur der Zustand aus der Tuya-Cloud übertragen.
        tuya.MapPost("/test-device/sync", SyncTestDevice).RequireAuthorization();

        // Alle Geräte, die mit einem Tuya-Gerät verknüpft sind. Jedes Gerät ist ein Aufruf bei
        // Tuya (Kontingent des Cloud-Projekts), deshalb nur für angemeldete Benutzer.
        tuya.MapGet("/linked-devices/status", GetLinkedDevicesStatus).RequireAuthorization();
        tuya.MapPost("/sync", SyncLinkedDevices).RequireAuthorization();
    }

    // GET /api/tuya/linked-devices/status – Rohwerte aller verknüpften Geräte, nichts wird gespeichert.
    // Dient dazu, die echten Statuscodes der eigenen Geräte zu sehen (Temperatur, Watt, Akku ...).
    private static async Task<IResult> GetLinkedDevicesStatus(
        TuyaDeviceSyncer syncer,
        IOptionsMonitor<TuyaOptions> options,
        CancellationToken cancellationToken)
    {
        var missing = options.CurrentValue.GetMissingCredentials();
        if (missing.Count > 0)
        {
            return NotConfigured(missing);
        }

        var results = await syncer.ReadAllAsync(apply: false, cancellationToken);
        return Results.Ok(new TuyaLinkedDevicesResponse(DateTimeOffset.UtcNow, results, StatusNote));
    }

    // POST /api/tuya/sync – liest alle verknüpften Geräte und überträgt bekannte Zustände
    // (switch_1, doorcontact_state) auf unsere Geräte. Schaltet nichts.
    private static async Task<IResult> SyncLinkedDevices(
        TuyaDeviceSyncer syncer,
        IOptionsMonitor<TuyaOptions> options,
        CancellationToken cancellationToken)
    {
        var missing = options.CurrentValue.GetMissingCredentials();
        if (missing.Count > 0)
        {
            return NotConfigured(missing);
        }

        var results = await syncer.ReadAllAsync(apply: true, cancellationToken);
        return Results.Ok(new TuyaLinkedDevicesResponse(DateTimeOffset.UtcNow, results, StatusNote));
    }

    // GET /api/tuya/devices – alle Geräte aus der Tuya-Cloud (verknüpfte App-Konten) und dazu,
    // mit welchem Gerät in unserer Datenbank sie schon verknüpft sind. Nur lesend.
    private static async Task<IResult> GetCloudDevices(
        TuyaClient client,
        ApplicationDbContext db,
        IOptionsMonitor<TuyaOptions> options,
        ILogger<TuyaClient> logger,
        CancellationToken cancellationToken)
    {
        var missing = options.CurrentValue.GetMissingCredentials();
        if (missing.Count > 0)
        {
            return NotConfigured(missing);
        }

        var (cloudDevices, problem) = await CallTuyaAsync(
            () => client.GetLinkedAppDevicesAsync(cancellationToken), logger, cancellationToken);
        if (problem is not null)
        {
            return problem;
        }

        // Welche Tuya-Geräte sind schon mit einem unserer Geräte verknüpft?
        var links = await db.Devices
            .Where(d => d.TuyaDeviceId != null)
            .Select(d => new { d.TuyaDeviceId, d.Id, d.Name })
            .ToDictionaryAsync(d => d.TuyaDeviceId!, cancellationToken);

        var response = cloudDevices!
            .OrderBy(d => d.Name, StringComparer.CurrentCultureIgnoreCase)
            .Select(d =>
            {
                var link = links.GetValueOrDefault(d.Id);
                return new TuyaCloudDeviceResponse(d.Id, d.Name, d.ProductName, d.Category, d.Online, link?.Id, link?.Name);
            })
            .ToList();

        return Results.Ok(response);
    }

    // GET /api/tuya/test-device/status – Status des konfigurierten Testgeräts aus der Tuya-Cloud
    private static async Task<IResult> GetTestDeviceStatus(
        TuyaClient client,
        IOptionsMonitor<TuyaOptions> options,
        ILogger<TuyaClient> logger,
        CancellationToken cancellationToken)
    {
        var (status, problem) = await ReadTestDeviceStatusAsync(client, options.CurrentValue, logger, cancellationToken);
        if (problem is not null)
        {
            return problem;
        }

        return Results.Ok(new TuyaDeviceStatusResponse(DateTimeOffset.UtcNow, status!, StatusNote));
    }

    // POST /api/tuya/test-device/sync – Status lesen und auf das verknüpfte Gerät übertragen.
    // Hat sich switch_1 geändert, werden IsActive und LastValue gesetzt und ein Ereignis gespeichert.
    private static async Task<IResult> SyncTestDevice(
        TuyaClient client,
        TuyaSwitchRecorder recorder,
        IOptionsMonitor<TuyaOptions> options,
        ILogger<TuyaClient> logger,
        CancellationToken cancellationToken)
    {
        var settings = options.CurrentValue;
        var (status, problem) = await ReadTestDeviceStatusAsync(client, settings, logger, cancellationToken);
        if (problem is not null)
        {
            return problem;
        }

        var retrievedAtUtc = DateTimeOffset.UtcNow;
        var syncResult = await recorder.RecordAsync(settings.LinkedDeviceId, status!, retrievedAtUtc, cancellationToken);

        return Results.Ok(new TuyaDeviceSyncResponse(retrievedAtUtc, status!, StatusNote, syncResult));
    }

    // Liest den Status des Testgeräts. Liefert entweder den Status oder eine fertige
    // Fehlerantwort (ProblemDetails), nie beides. So nutzen GET und POST dieselbe Fehlerbehandlung.
    private static async Task<(List<TuyaStatusItem>? Status, IResult? Problem)> ReadTestDeviceStatusAsync(
        TuyaClient client,
        TuyaOptions settings,
        ILogger<TuyaClient> logger,
        CancellationToken cancellationToken)
    {
        // Fehlt etwas, nennen wir nur die Namen, nie die Werte.
        var missing = settings.GetMissingSettings();
        if (missing.Count > 0)
        {
            return (null, NotConfigured(missing));
        }

        return await CallTuyaAsync(
            () => client.GetDeviceStatusAsync(settings.TestDeviceId, cancellationToken), logger, cancellationToken);
    }

    // 503, wenn Zugangsdaten fehlen. Genannt werden nur die Namen der Einstellungen.
    private static IResult NotConfigured(List<string> missing)
    {
        return Results.Problem(
            statusCode: StatusCodes.Status503ServiceUnavailable,
            title: "Tuya ist nicht eingerichtet.",
            detail: $"Es fehlen folgende Einstellungen in den User Secrets: {string.Join(", ", missing)}.");
    }

    // Führt einen Aufruf an die Tuya-Cloud aus und übersetzt Fehler in verständliche
    // ProblemDetails. Liefert entweder das Ergebnis oder die Fehlerantwort, nie beides.
    private static async Task<(T? Value, IResult? Problem)> CallTuyaAsync<T>(
        Func<Task<T>> call,
        ILogger<TuyaClient> logger,
        CancellationToken cancellationToken)
        where T : class
    {
        try
        {
            return (await call(), null);
        }
        catch (TuyaApiException ex)
        {
            // Code und Meldung kommen von Tuya und enthalten keine Geheimnisse.
            logger.LogWarning("Tuya hat die Anfrage abgelehnt: {TuyaCode} {TuyaMessage}", ex.TuyaCode, ex.TuyaMessage);
            return (null, Results.Problem(
                statusCode: StatusCodes.Status502BadGateway,
                title: "Tuya hat die Anfrage abgelehnt.",
                detail: $"Tuya-Fehler {ex.TuyaCode}: {ex.TuyaMessage}"));
        }
        catch (HttpRequestException ex)
        {
            logger.LogWarning("Tuya-Cloud nicht erreichbar: {Message}", ex.Message);
            return (null, Results.Problem(
                statusCode: StatusCodes.Status502BadGateway,
                title: "Die Tuya-Cloud ist nicht erreichbar.",
                detail: "Bitte Internetverbindung prüfen und später erneut versuchen."));
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // Zeitüberschreitung des HttpClient (nicht vom Client abgebrochen)
            return (null, Results.Problem(
                statusCode: StatusCodes.Status504GatewayTimeout,
                title: "Die Tuya-Cloud antwortet nicht.",
                detail: "Die Anfrage an Tuya hat zu lange gedauert."));
        }
        catch (JsonException ex)
        {
            logger.LogWarning("Unerwartete Antwort von Tuya: {Message}", ex.Message);
            return (null, Results.Problem(
                statusCode: StatusCodes.Status502BadGateway,
                title: "Unerwartete Antwort von Tuya.",
                detail: "Die Antwort der Tuya-Cloud hatte nicht das erwartete Format."));
        }
    }
}
