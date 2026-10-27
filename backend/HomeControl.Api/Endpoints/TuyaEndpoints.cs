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

    // Registriert die Endpunkte unter /api/tuya. Nur lesend: es wird nichts geschaltet
    // und nichts in der Datenbank gespeichert.
    public static void MapTuyaEndpoints(this WebApplication app)
    {
        var tuya = app.MapGroup("/api/tuya");

        tuya.MapGet("/test-device/status", GetTestDeviceStatus);
    }

    // GET /api/tuya/test-device/status – Status des konfigurierten Testgeräts aus der Tuya-Cloud
    private static async Task<IResult> GetTestDeviceStatus(
        TuyaClient client,
        IOptionsMonitor<TuyaOptions> options,
        ILogger<TuyaClient> logger,
        CancellationToken cancellationToken)
    {
        // Aktuelle Einstellungen lesen.
        // Fehlt etwas, nennen wir nur die Namen, nie die Werte.
        var settings = options.CurrentValue;
        var missing = settings.GetMissingSettings();
        if (missing.Count > 0)
        {
            return Results.Problem(
                statusCode: StatusCodes.Status503ServiceUnavailable,
                title: "Tuya ist nicht eingerichtet.",
                detail: $"Es fehlen folgende Einstellungen in den User Secrets: {string.Join(", ", missing)}.");
        }

        try
        {
            var status = await client.GetDeviceStatusAsync(settings.TestDeviceId, cancellationToken);
            return Results.Ok(new TuyaDeviceStatusResponse(DateTimeOffset.UtcNow, status, StatusNote));
        }
        catch (TuyaApiException ex)
        {
            // Code und Meldung kommen von Tuya und enthalten keine Geheimnisse.
            logger.LogWarning("Tuya hat die Anfrage abgelehnt: {TuyaCode} {TuyaMessage}", ex.TuyaCode, ex.TuyaMessage);
            return Results.Problem(
                statusCode: StatusCodes.Status502BadGateway,
                title: "Tuya hat die Anfrage abgelehnt.",
                detail: $"Tuya-Fehler {ex.TuyaCode}: {ex.TuyaMessage}");
        }
        catch (HttpRequestException ex)
        {
            logger.LogWarning("Tuya-Cloud nicht erreichbar: {Message}", ex.Message);
            return Results.Problem(
                statusCode: StatusCodes.Status502BadGateway,
                title: "Die Tuya-Cloud ist nicht erreichbar.",
                detail: "Bitte Internetverbindung prüfen und später erneut versuchen.");
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // Zeitüberschreitung des HttpClient (nicht vom Client abgebrochen)
            return Results.Problem(
                statusCode: StatusCodes.Status504GatewayTimeout,
                title: "Die Tuya-Cloud antwortet nicht.",
                detail: "Die Anfrage an Tuya hat zu lange gedauert.");
        }
        catch (JsonException ex)
        {
            logger.LogWarning("Unerwartete Antwort von Tuya: {Message}", ex.Message);
            return Results.Problem(
                statusCode: StatusCodes.Status502BadGateway,
                title: "Unerwartete Antwort von Tuya.",
                detail: "Die Antwort der Tuya-Cloud hatte nicht das erwartete Format.");
        }
    }
}
