using System.Text.Json;
using HomeControl.Api.Contracts;
using HomeControl.Api.Tuya;
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

        tuya.MapGet("/test-device/status", GetTestDeviceStatus);
        tuya.MapPost("/test-device/sync", SyncTestDevice);
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
            return (null, Results.Problem(
                statusCode: StatusCodes.Status503ServiceUnavailable,
                title: "Tuya ist nicht eingerichtet.",
                detail: $"Es fehlen folgende Einstellungen in den User Secrets: {string.Join(", ", missing)}."));
        }

        try
        {
            var status = await client.GetDeviceStatusAsync(settings.TestDeviceId, cancellationToken);
            return (status, null);
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
