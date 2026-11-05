using System.Net.ServerSentEvents;
using System.Runtime.CompilerServices;
using System.Security.Claims;
using System.Threading.Channels;
using HomeControl.Api.Auth;
using HomeControl.Api.Realtime;
using Microsoft.IdentityModel.JsonWebTokens;

namespace HomeControl.Api.Endpoints;

public static class RealtimeEndpoints
{
    public const string ConnectedEvent = "connected";
    public const string ChangeEvent = "device-change";
    public const string PingEvent = "ping";

    // Lebenszeichen: Hält die Verbindung durch Proxys offen (z. B. tailscale serve) und zeigt dem Server früh,
    // wenn der Browser weg ist.
    public static readonly TimeSpan Heartbeat = TimeSpan.FromSeconds(25);

    // Falls ein Token keinen Ablauf nennt (kommt bei uns nicht vor): trotzdem nicht ewig offen lassen.
    private static readonly TimeSpan FallbackLifetime = TimeSpan.FromMinutes(15);

    // GET /api/live/devices – Server-Sent Events: Der Server meldet dem Browser geänderte Geräte, sobald ER sie kennt.
    // Das macht aus dem Tuya-Polling noch keinen Tuya-Push (docs/ECHTZEIT.md). Nur Menschen mit JWT, kein Agenten-Schlüssel.
    public static void MapRealtimeEndpoints(this WebApplication app)
    {
        app.MapGet("/api/live/devices", StreamDeviceChanges)
            .RequireAuthorization(AuthPolicies.SignedInUser)
            .WithTags("Status")
            .WithSummary("Server-Sent Events: Welche Geräte haben sich geändert?");
    }

    private static IResult StreamDeviceChanges(
        ClaimsPrincipal user,
        HttpContext http,
        DeviceChangeHub hub,
        TimeProvider timeProvider,
        IHostApplicationLifetime lifetime)
    {
        // Die Anmeldung wird nur beim Verbindungsaufbau geprüft. Damit niemand mit einem längst abgelaufenen Token
        // weiterlauscht, endet der Strom beim Ablauf des Tokens. Der Browser verbindet sich neu; ist das Token schon
        // abgelaufen (die Prüfung lässt 30 s Spielraum), bekommt er hier 401 und holt sich per Refresh ein neues.
        var now = timeProvider.GetUtcNow();
        var closeAtUtc = TokenExpiry(user) ?? now + FallbackLifetime;
        if (closeAtUtc <= now)
        {
            return Results.Unauthorized();
        }

        var subscription = hub.TrySubscribe();
        if (subscription is null)
        {
            return Results.Problem(statusCode: StatusCodes.Status503ServiceUnavailable,
                title: "Zu viele Live-Verbindungen. Die Seite aktualisiert sich weiter jede Minute.");
        }

        // Auch beim Beenden der App schließen, sonst wartet der Dienst bis zu 30 s auf die offenen Verbindungen.
        var stop = CancellationTokenSource.CreateLinkedTokenSource(http.RequestAborted, lifetime.ApplicationStopping);
        http.Response.RegisterForDispose(stop);
        // Doppelt abgesichert: Bricht die Antwort ab, bevor der Strom überhaupt läuft, wird der Platz trotzdem frei.
        http.Response.RegisterForDispose(subscription);

        return TypedResults.ServerSentEvents(ReadEventsAsync(subscription, closeAtUtc, timeProvider, stop.Token));
    }

    private static DateTimeOffset? TokenExpiry(ClaimsPrincipal user) =>
        long.TryParse(user.FindFirst(JwtRegisteredClaimNames.Exp)?.Value, out var seconds)
            ? DateTimeOffset.FromUnixTimeSeconds(seconds)
            : null;

    // Erst "connected", dann Änderungen sobald vorhanden, dazwischen Lebenszeichen. Endet bei closeAtUtc oder Abbruch.
    // "connected" nach einer Unterbrechung heißt für den Browser: neu laden, es könnte etwas verpasst worden sein.
    public static async IAsyncEnumerable<SseItem<DeviceChangeEvent?>> ReadEventsAsync(
        DeviceChangeSubscription subscription,
        DateTimeOffset closeAtUtc,
        TimeProvider timeProvider,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        using (subscription)
        {
            yield return new SseItem<DeviceChangeEvent?>(null, ConnectedEvent);

            while (true)
            {
                var remaining = closeAtUtc - timeProvider.GetUtcNow();
                if (remaining <= TimeSpan.Zero)
                {
                    yield break;
                }

                var wait = remaining < Heartbeat ? remaining : Heartbeat;
                switch (await WaitForChangeAsync(subscription.Reader, wait, timeProvider, cancellationToken))
                {
                    case WaitResult.Aborted:
                        yield break;
                    case WaitResult.TimedOut:
                        yield return new SseItem<DeviceChangeEvent?>(null, PingEvent);
                        break;
                    default:
                        while (subscription.Reader.TryRead(out var change))
                        {
                            yield return new SseItem<DeviceChangeEvent?>(change, ChangeEvent);
                        }

                        break;
                }
            }
        }
    }

    private enum WaitResult
    {
        HasData,
        TimedOut,
        Aborted,
    }

    // Eigene Methode, weil C# in einem try/catch kein "yield return" erlaubt.
    private static async Task<WaitResult> WaitForChangeAsync(
        ChannelReader<DeviceChangeEvent> reader,
        TimeSpan wait,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        using var timeout = new CancellationTokenSource(wait, timeProvider);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(timeout.Token, cancellationToken);
        try
        {
            return await reader.WaitToReadAsync(linked.Token) ? WaitResult.HasData : WaitResult.Aborted;
        }
        catch (OperationCanceledException)
        {
            return cancellationToken.IsCancellationRequested ? WaitResult.Aborted : WaitResult.TimedOut;
        }
    }
}
