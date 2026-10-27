using HomeControl.Api.Blink;
using HomeControl.Api.Contracts;

namespace HomeControl.Api.Endpoints;

public static class BlinkEndpoints
{
    private const string StatusNote =
        "Blink hat keine offizielle API (reverse engineered). Kamera-Felder wie Akku, " +
        "Temperatur und Online-Status sind \"best effort\" und noch nicht gegen einen echten " +
        "Account geprüft, siehe docs/BLINK.md.";

    // Registriert die Endpunkte unter /api/blink. Nur lesend: kein Schalten, kein Scharf-/
    // Unscharfschalten, kein Löschen von Clips. Der Thumbnail-Endpunkt folgt erst nach einem
    // API-Schlüssel für Home Control, siehe docs/BLINK.md.
    public static void MapBlinkEndpoints(this WebApplication app)
    {
        var blink = app.MapGroup("/api/blink");

        blink.MapGet("/status", GetStatus);
        blink.MapGet("/cameras", GetCameras);
    }

    private static async Task<IResult> GetStatus(IBlinkCameraClient client, CancellationToken cancellationToken)
    {
        var status = await client.GetStatusAsync(cancellationToken);
        var state = status.State switch
        {
            BlinkConnectionState.Connected => "connected",
            BlinkConnectionState.LoginRequired => "loginRequired",
            _ => "notConfigured",
        };

        return Results.Ok(new BlinkStatusResponse(state, status.LastSuccessfulFetchUtc, StatusNote));
    }

    private static async Task<IResult> GetCameras(
        IBlinkCameraClient client,
        ILogger<IBlinkCameraClient> logger,
        CancellationToken cancellationToken)
    {
        try
        {
            var cameras = await client.GetCamerasAsync(cancellationToken);
            var response = cameras
                .Select(c => new BlinkCameraResponse(c.Id, c.Name, c.Battery, c.TemperatureCelsius, c.Online))
                .ToList();

            return Results.Ok(response);
        }
        catch (BlinkNotConfiguredException ex)
        {
            return Results.Problem(
                statusCode: StatusCodes.Status503ServiceUnavailable,
                title: "Blink ist nicht eingerichtet.",
                detail: ex.Message);
        }
        catch (BlinkLoginRequiredException ex)
        {
            logger.LogWarning("Blink-Anmeldung nötig: {Message}", ex.Message);
            return Results.Problem(
                statusCode: StatusCodes.Status503ServiceUnavailable,
                title: "Blink-Anmeldung nötig.",
                detail: ex.Message);
        }
        catch (BlinkApiException ex)
        {
            logger.LogWarning("Blink hat die Anfrage abgelehnt: {Message}", ex.Message);
            return Results.Problem(
                statusCode: StatusCodes.Status502BadGateway,
                title: "Blink hat die Anfrage abgelehnt.",
                detail: ex.Message);
        }
    }
}
