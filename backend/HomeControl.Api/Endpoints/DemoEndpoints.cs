using HomeControl.Api.Contracts;
using HomeControl.Api.Demo;

namespace HomeControl.Api.Endpoints;

public static class DemoEndpoints
{
    // GET /api/demo – Für das Banner "Demo-Modus – Beispieldaten". Ohne Anmeldung (AllowAnonymous), weil das
    // Banner schon auf der Anmeldeseite stehen soll. Verrät nur, OB Demo läuft, keine Daten.
    public static void MapDemoEndpoints(this WebApplication app)
    {
        app.MapGet("/api/demo", (IConfiguration configuration) =>
            {
                var demoMode = DemoMode.IsEnabled(configuration);
                return Results.Ok(new DemoInfoResponse(demoMode, demoMode ? DemoHouse.Email : null));
            })
            .AllowAnonymous()
            .WithTags("Status")
            .WithSummary("Läuft diese Instanz im Demo-Modus (Beispieldaten)?");
    }
}
