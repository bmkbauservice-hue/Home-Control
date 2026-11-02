using System.Globalization;
using HomeControl.Api.Auth;
using HomeControl.Api.Blink;
using HomeControl.Api.Contracts;
using HomeControl.Api.RateLimiting;

namespace HomeControl.Api.Endpoints;

public static class BlinkEndpoints
{
    private const string StatusNote =
        "Blink hat keine offizielle API (reverse engineered). Online kommt aus dem Feld \"status\" " +
        "(nur \"online\"/\"offline\", sonst unbekannt), die Temperatur ist noch nicht geprüft, siehe docs/BLINK.md.";

    // Registriert die Endpunkte unter /api/blink. Nur lesend: kein Schalten, kein Scharf-/
    // Unscharfschalten, kein Löschen von Clips. Der Thumbnail-Endpunkt folgt erst nach einem
    // API-Schlüssel für Home Control, siehe docs/BLINK.md.
    //
    // Beide Endpunkte können einen echten Abruf bei Blink auslösen (höchstens alle zwei
    // Minuten, siehe BlinkHttpCameraClient.MinPollInterval), deshalb behandeln beide dieselben
    // Fehler aus dem HTTP-Zugriff (nicht erreichbar, Zeitüberschreitung).
    public static void MapBlinkEndpoints(this WebApplication app)
    {
        var blink = app.MapGroup("/api/blink").WithTags("Blink");

        blink.MapGet("/status", GetStatus);
        blink.MapGet("/cameras", GetCameras);

        // Letztes Vorschaubild einer Kamera. Das Backend holt es mit SEINEM Blink-Token und reicht nur das Bild
        // weiter: Token und Blink-Adressen erreichen den Browser nie (Proxy statt Direktlink).
        // Nur für angemeldete Menschen (JWT), nie mit dem Agenten-Schlüssel. Eigene Rate-Limit-Regel.
        blink.MapGet("/cameras/{id:long}/thumbnail", GetThumbnail)
            .RequireAuthorization(AuthPolicies.SignedInUser)
            .RequireRateLimiting(RateLimitPolicies.BlinkThumbnails)
            .WithSummary("Letztes Vorschaubild einer Kamera (image/jpeg, nur mit Anmeldung)");

        // BEWUSSTE AUSNAHME von "nur lesen" (docs/BLINK.md, CLAUDE.md): Die Kamera nimmt ein neues Vorschaubild auf.
        // Kostet Akku, schaltet aber nichts. Nur Admin (JWT), höchstens 1× pro Minute und Kamera.
        blink.MapPost("/cameras/{id:long}/snapshot", RequestSnapshot)
            .RequireAuthorization(AuthPolicies.AdminOnly)
            .RequireRateLimiting(RateLimitPolicies.BlinkSnapshot)
            .WithSummary("Neues Vorschaubild aufnehmen lassen (nur Admin, 1× pro Minute und Kamera, kostet Akku)");
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
                BlinkConnectionState.Disabled => "disabled",
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
                .Select(c => new BlinkCameraResponse(c.Id, c.Name, c.Battery, c.TemperatureCelsius, c.Online,
                    c.HasThumbnail, c.ThumbnailTakenAtUtc))
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
        catch (BlinkDisabledException ex)
        {
            return Results.Problem(
                statusCode: StatusCodes.Status503ServiceUnavailable,
                title: "Blink ist in dieser Umgebung ausgeschaltet.",
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

    // POST /api/blink/cameras/{id}/snapshot
    private static async Task<IResult> RequestSnapshot(
        long id,
        IBlinkCameraClient client,
        ILogger<IBlinkCameraClient> logger,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await client.RequestNewThumbnailAsync(id.ToString(CultureInfo.InvariantCulture), cancellationToken);
            return result is null
                ? Results.Problem(statusCode: StatusCodes.Status404NotFound, title: "Diese Kamera gibt es nicht.")
                : Results.Ok(new BlinkSnapshotResponse(result.Completed, result.ThumbnailTakenAtUtc));
        }
        catch (BlinkDisabledException ex)
        {
            return Results.Problem(statusCode: StatusCodes.Status503ServiceUnavailable,
                title: "Blink ist in dieser Umgebung ausgeschaltet.", detail: ex.Message);
        }
        catch (BlinkNotConfiguredException ex)
        {
            return Results.Problem(statusCode: StatusCodes.Status503ServiceUnavailable,
                title: "Blink ist nicht eingerichtet.", detail: ex.Message);
        }
        catch (BlinkLoginRequiredException ex)
        {
            logger.LogWarning("Blink-Anmeldung nötig ({ExceptionType}): {Message}", ex.GetType().Name, ex.Message);
            return Results.Problem(statusCode: StatusCodes.Status503ServiceUnavailable,
                title: "Blink-Anmeldung nötig.", detail: ex.Message);
        }
        catch (Exception ex) when (TryMapTransportError(ex, logger, cancellationToken, out var problem))
        {
            return problem!;
        }
    }

    // GET /api/blink/cameras/{id}/thumbnail
    private static async Task<IResult> GetThumbnail(
        long id,
        IBlinkCameraClient client,
        HttpContext http,
        ILogger<IBlinkCameraClient> logger,
        CancellationToken cancellationToken)
    {
        try
        {
            var thumbnail = await client.GetThumbnailAsync(id.ToString(CultureInfo.InvariantCulture), cancellationToken);
            if (thumbnail is null)
            {
                return Results.Problem(
                    statusCode: StatusCodes.Status404NotFound,
                    title: "Für diese Kamera gibt es kein Vorschaubild.");
            }

            // no-store: Kamerabilder nicht im Browser-Cache oder in einem Proxy ablegen (Datenschutz).
            http.Response.Headers.CacheControl = "no-store";
            if (thumbnail.TakenAtUtc is { } takenAt)
            {
                // Aufnahmezeitpunkt als Header, damit das Frontend "Aufnahme von …" zeigen kann.
                http.Response.Headers["X-Thumbnail-Taken-At"] = takenAt.ToString("O", CultureInfo.InvariantCulture);
            }

            return Results.File(thumbnail.Content, thumbnail.ContentType);
        }
        catch (BlinkDisabledException ex)
        {
            return Results.Problem(statusCode: StatusCodes.Status503ServiceUnavailable,
                title: "Blink ist in dieser Umgebung ausgeschaltet.", detail: ex.Message);
        }
        catch (BlinkNotConfiguredException ex)
        {
            return Results.Problem(statusCode: StatusCodes.Status503ServiceUnavailable,
                title: "Blink ist nicht eingerichtet.", detail: ex.Message);
        }
        catch (BlinkLoginRequiredException ex)
        {
            logger.LogWarning("Blink-Anmeldung nötig ({ExceptionType}): {Message}", ex.GetType().Name, ex.Message);
            return Results.Problem(statusCode: StatusCodes.Status503ServiceUnavailable,
                title: "Blink-Anmeldung nötig.", detail: ex.Message);
        }
        catch (Exception ex) when (TryMapTransportError(ex, logger, cancellationToken, out var problem))
        {
            return problem!;
        }
    }

    // Deckt Fehler aus dem eigentlichen HTTP-Zugriff ab, die beide Endpunkte gleich behandeln:
    // eine BlinkApiException (Blink lehnt ab, z. B. 426), eine unerreichbare Blink-Cloud
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
