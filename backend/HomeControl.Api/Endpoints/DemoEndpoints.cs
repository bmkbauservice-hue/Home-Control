using HomeControl.Api.Auth;
using HomeControl.Api.Contracts;
using HomeControl.Api.Data;
using HomeControl.Api.Demo;
using HomeControl.Api.Tuya;
using Microsoft.EntityFrameworkCore;

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

        // Nur im Demo-Modus vorhanden. Im Dienst gibt es diesen Endpunkt gar nicht (404).
        if (DemoMode.IsEnabled(app.Configuration))
        {
            app.MapPost("/api/demo/simulation/tuer", ToggleDoor)
                .RequireAuthorization(AuthPolicies.AdminOnly)
                .WithTags("Status")
                .WithSummary("Nur Demo: Haustür simuliert öffnen oder schließen, Änderung läuft durch den echten Abgleich");
        }
    }

    // Schaltet die SIMULIERTE Tür um und gleicht sie sofort ab. Dabei läuft der echte Weg:
    // Fake-Cloud → TuyaClient → TuyaDeviceSyncer → Datenbank → DeviceChangeHub → Browser (docs/ECHTZEIT.md).
    private static async Task<IResult> ToggleDoor(
        ApplicationDbContext db,
        DemoSimulation simulation,
        TuyaDeviceSyncer syncer,
        CancellationToken cancellationToken)
    {
        var doorTuyaId = DemoHouse.Devices.Single(d => d.Kind == DemoKind.Door).TuyaId;
        var door = await db.Devices.AsNoTracking().FirstOrDefaultAsync(d => d.TuyaDeviceId == doorTuyaId, cancellationToken);
        if (door is null)
        {
            return Results.Problem(statusCode: StatusCodes.Status404NotFound, title: "Die Demo-Haustür gibt es nicht mehr.");
        }

        simulation.SetDoor(door.LastValue != TuyaDeviceSyncer.OpenText);
        await syncer.ReadAsync(apply: true, [door.Id], refreshOnline: false, cancellationToken);
        return Results.NoContent();
    }
}
