using System.Net;
using System.Security.Cryptography;
using System.Text;

namespace HomeControl.Api.Climate;

// Gemeinsame Absicherung aller Aufrufe von Muju Home: nur von diesem Rechner und nur mit dem Bridge-Schlüssel.
public static class SensorBridgeGuard
{
    // null = in Ordnung. Sonst die passende Fehlerantwort.
    public static IResult? Reject(HttpContext context, IConfiguration config)
    {
        // Das Backend lauscht nur auf Loopback. Für das Handy gibt es den eigenen Gateway.
        var remote = context.Connection.RemoteIpAddress;
        if (remote is null || !IPAddress.IsLoopback(remote))
            return Results.StatusCode(StatusCodes.Status403Forbidden);

        var key = config["SensorBridge:Key"];
        if (string.IsNullOrEmpty(key) || key.Length < 32)
            return Results.Problem(statusCode: 503, title: "Sensor-Bridge ist noch nicht eingerichtet.");

        // Vergleich in fester Zeit, damit die Antwortzeit nichts über den Schlüssel verrät.
        var actual = Encoding.UTF8.GetBytes(context.Request.Headers["X-Sensor-Key"].ToString());
        var expected = Encoding.UTF8.GetBytes(key);
        return actual.Length != expected.Length || !CryptographicOperations.FixedTimeEquals(actual, expected)
            ? Results.Unauthorized()
            : null;
    }
}
