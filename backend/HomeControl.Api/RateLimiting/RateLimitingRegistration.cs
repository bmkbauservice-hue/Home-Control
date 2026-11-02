using System.Globalization;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;

namespace HomeControl.Api.RateLimiting;

// Namen der Rate-Limiting-Regeln (Policies). Benutzt an den Endpunkten mit
// .RequireRateLimiting(RateLimitPolicies.Login), ähnlich wie AuthPolicies.
public static class RateLimitPolicies
{
    // POST /api/auth/login, /register und /refresh. Gezählt pro Client-IP.
    public const string Login = "login";

    // Alle Endpunkte unter /api/tuya (echte Aufrufe bei der Tuya-Cloud). Ein Zähler für alle.
    public const string Tuya = "tuya";

    // GET /api/blink/cameras/{id}/thumbnail. Gezählt pro Client-IP.
    public const string BlinkThumbnails = "blink-bilder";
}

// Richtet den eingebauten Rate Limiter von ASP.NET Core ein (REVIEW.md M-2, M-3).
// Die Middleware dazu (app.UseRateLimiter) steht in Program.cs.
public static class RateLimitingRegistration
{
    public static IServiceCollection AddHomeControlRateLimiting(this IServiceCollection services, IConfiguration configuration)
    {
        // Grenzen aus appsettings.json. Unsinnige Werte (0, negativ) verhindern den Start,
        // statt erst bei der ersten Anfrage aufzufallen.
        services.AddOptions<RateLimitingOptions>()
            .Bind(configuration.GetSection(RateLimitingOptions.SectionName))
            .Validate(o => o.Login.IsValid && o.Tuya.IsValid && o.BlinkThumbnails.IsValid,
                "RateLimiting: PermitLimit und WindowSeconds müssen größer als 0 sein.")
            .ValidateOnStart();

        services.AddRateLimiter(options =>
        {
            // Standard wäre 503. 429 "Too Many Requests" ist der richtige Code für "zu viele Anfragen".
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

            // Pro Gerät: Der Schlüssel (Partition) ist die IP des Clients. Hinter "tailscale serve"
            // ist das dank UseForwardedHeaders (Program.cs) die echte Tailscale-IP des Geräts,
            // nicht 127.0.0.1. So kann ein Gerät mit vielen Fehlversuchen die anderen nicht aussperren.
            options.AddPolicy(RateLimitPolicies.Login, context =>
            {
                var limit = GetOptions(context).Login;
                var client = context.Connection.RemoteIpAddress?.ToString() ?? "unbekannt";
                return RateLimitPartition.GetFixedWindowLimiter(client, _ => CreateFixedWindow(limit));
            });

            // Für alle gemeinsam: immer derselbe Schlüssel. Geschützt wird das Tuya-Kontingent, und das
            // ist gleich schnell verbraucht, egal von wie vielen Geräten die Anfragen kommen.
            options.AddPolicy(RateLimitPolicies.Tuya, context =>
            {
                var limit = GetOptions(context).Tuya;
                return RateLimitPartition.GetFixedWindowLimiter("alle", _ => CreateFixedWindow(limit));
            });

            options.AddPolicy(RateLimitPolicies.BlinkThumbnails, context =>
            {
                var limit = GetOptions(context).BlinkThumbnails;
                var client = context.Connection.RemoteIpAddress?.ToString() ?? "unbekannt";
                return RateLimitPartition.GetFixedWindowLimiter(client, _ => CreateFixedWindow(limit));
            });

            options.OnRejected = WriteRejectionAsync;
        });

        return services;
    }

    // Gelesen wird erst bei der Anfrage, nicht beim Start. So kommen auch Einstellungen aus den Tests an.
    private static RateLimitingOptions GetOptions(HttpContext context) =>
        context.RequestServices.GetRequiredService<IOptions<RateLimitingOptions>>().Value;

    // Fixed Window: Ein Zähler pro Zeitfenster, der am Ende des Fensters wieder auf 0 springt.
    // QueueLimit 0: Überzählige Anfragen warten nicht in einer Schlange, sie bekommen sofort 429.
    private static FixedWindowRateLimiterOptions CreateFixedWindow(FixedWindowLimit limit) => new()
    {
        PermitLimit = limit.PermitLimit,
        Window = TimeSpan.FromSeconds(limit.WindowSeconds),
        QueueLimit = 0,
        AutoReplenishment = true,
    };

    // Antwort bei Überschreitung: 429 als ProblemDetails mit deutschem Text und Header Retry-After
    // (in wie vielen Sekunden es der Client wieder versuchen darf).
    private static async ValueTask WriteRejectionAsync(OnRejectedContext context, CancellationToken cancellationToken)
    {
        var http = context.HttpContext;

        // Der Fixed-Window-Limiter weiß, wann das aktuelle Fenster endet. Aufgerundet, mindestens 1 Sekunde.
        int? retryAfterSeconds = null;
        if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
        {
            retryAfterSeconds = Math.Max(1, (int)Math.Ceiling(retryAfter.TotalSeconds));
            http.Response.Headers.RetryAfter = retryAfterSeconds.Value.ToString(CultureInfo.InvariantCulture);
        }

        // Welche Regel hat gegriffen? Steht in den Metadaten des Endpunkts (RequireRateLimiting).
        var policy = http.GetEndpoint()?.Metadata.GetMetadata<EnableRateLimitingAttribute>()?.PolicyName;

        // Ins Log: Regel, Adresse und Herkunft. Keine Inhalte der Anfrage (dort könnte ein Passwort stehen).
        var logger = http.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(RateLimitingRegistration));
        logger.LogWarning("Rate Limit {Policy} überschritten: {Method} {Path} von {RemoteIp}",
            policy, http.Request.Method, http.Request.Path, http.Connection.RemoteIpAddress);

        var wait = retryAfterSeconds is { } seconds ? $"in {seconds} Sekunden" : "gleich";
        var title = policy switch
        {
            RateLimitPolicies.Login => $"Zu viele Anmeldeversuche. Bitte {wait} erneut versuchen.",
            RateLimitPolicies.Tuya => $"Zu viele Anfragen an die Tuya-Cloud. Bitte {wait} erneut versuchen.",
            RateLimitPolicies.BlinkThumbnails => $"Zu viele Kamerabilder angefordert. Bitte {wait} erneut versuchen.",
            _ => $"Zu viele Anfragen. Bitte {wait} erneut versuchen.",
        };

        var problemDetails = http.RequestServices.GetRequiredService<IProblemDetailsService>();
        await problemDetails.WriteAsync(new ProblemDetailsContext
        {
            HttpContext = http,
            ProblemDetails = new ProblemDetails
            {
                Status = StatusCodes.Status429TooManyRequests,
                Title = title,
                Detail = "Diese Grenze schützt vor Passwort-Raten und davor, dass Tuya oder Blink mit Anfragen geflutet werden.",
            },
        });
    }
}
