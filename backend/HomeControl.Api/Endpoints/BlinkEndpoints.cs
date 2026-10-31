using HomeControl.Api.Blink;
using HomeControl.Api.Contracts;

namespace HomeControl.Api.Endpoints;

public static class BlinkEndpoints
{
    private const string StatusNote =
        "Blink hat keine offizielle API (reverse engineered). Kamera-Felder Online und " +
        "Temperatur sind vorerst immer leer, bis die Feldnamen gegen einen echten Account " +
        "geprüft sind, siehe docs/BLINK.md.";

    // Registriert die Endpunkte unter /api/blink. Nur lesend: kein Schalten, kein Scharf-/
    // Unscharfschalten, kein Löschen von Clips. Der Thumbnail-Endpunkt folgt erst nach einem
    // API-Schlüssel für Home Control, siehe docs/BLINK.md.
    //
    // Beide Endpunkte können einen echten Abruf bei Blink auslösen (höchstens alle zwei
    // Minuten, siehe BlinkNetCameraClient.MinPollInterval), deshalb behandeln beide dieselben
    // Fehler aus dem HTTP-Zugriff (nicht erreichbar, Zeitüberschreitung).
    public static void MapBlinkEndpoints(this WebApplication app)
    {
        var blink = app.MapGroup("/api/blink").WithTags("Blink");

        blink.MapGet("/status", GetStatus);
        blink.MapGet("/cameras", GetCameras);
    }

    private static async Task<IResult> GetStatus(
        IBlinkCameraClient client,
        ILogger<IBlinkCameraClient> logger,
        CancellationToken cancellationToken)
    {
        try
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
        catch (Exception ex) when (TryMapTransportError(ex, logger, cancellationToken, out var problem))
        {
            return problem!;
        }
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
            logger.LogWarning("Blink-Anmeldung nötig ({ExceptionType}): {Message}", ex.GetType().Name, ex.Message);
            return Results.Problem(
                statusCode: StatusCodes.Status503ServiceUnavailable,
                title: "Blink-Anmeldung nötig.",
                detail: ex.Message);
        }
        catch (Exception ex) when (TryMapTransportError(ex, logger, cancellationToken, out var problem))
        {
            return problem!;
        }
    }

    // Deckt Fehler aus dem eigentlichen HTTP-Zugriff ab, die beide Endpunkte gleich behandeln:
    // eine von Blink.NET geworfene BlinkApiException, eine unerreichbare Blink-Cloud
    // (HttpRequestException) oder eine Zeitüberschreitung (TaskCanceledException, aber nicht
    // durch den Client selbst abgebrochen). Gibt false zurück, wenn ex keiner dieser Fälle ist –
    // dann läuft die Exception unbehandelt weiter (kein stiller Fehlschluck).
    private static bool TryMapTransportError(
        Exception ex,
        ILogger logger,
        CancellationToken cancellationToken,
        out IResult? problem)
    {
        switch (ex)
        {
            case BlinkApiException apiEx:
                logger.LogWarning("Blink hat die Anfrage abgelehnt ({ExceptionType}): {Message}", apiEx.GetType().Name, apiEx.Message);
                problem = Results.Problem(
                    statusCode: StatusCodes.Status502BadGateway,
                    title: "Blink hat die Anfrage abgelehnt.",
                    detail: apiEx.Message);
                return true;

            case HttpRequestException httpEx:
                logger.LogWarning("Blink-Cloud nicht erreichbar ({ExceptionType}): {Message}", httpEx.GetType().Name, httpEx.Message);
                problem = Results.Problem(
                    statusCode: StatusCodes.Status502BadGateway,
                    title: "Die Blink-Cloud ist nicht erreichbar.",
                    detail: "Bitte Internetverbindung prüfen und später erneut versuchen.");
                return true;

            case TaskCanceledException when !cancellationToken.IsCancellationRequested:
                problem = Results.Problem(
                    statusCode: StatusCodes.Status504GatewayTimeout,
                    title: "Die Blink-Cloud antwortet nicht.",
                    detail: "Die Anfrage an Blink hat zu lange gedauert.");
                return true;

            default:
                problem = null;
                return false;
        }
    }
}
