using HomeControl.Api.Data;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace HomeControl.Api.Status;

// Gesundheitsprüfung für Docker (docs/DOCKER.md): GET /healthz antwortet "Healthy", wenn die App läuft und die
// Datenbank erreichbar ist. Verrät sonst nichts, deshalb ohne Anmeldung.
public class DatabaseHealthCheck(ApplicationDbContext db) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default) =>
        await db.Database.CanConnectAsync(cancellationToken)
            ? HealthCheckResult.Healthy()
            : HealthCheckResult.Unhealthy("Datenbank nicht erreichbar.");
}

// "dotnet HomeControl.Api.dll --healthcheck": fragt /healthz der laufenden App ab und endet mit 0 (gesund) oder 1.
// Warum so umständlich? Das schlanke aspnet-Image hat weder curl noch wget. Statt sie nachzuinstallieren
// (größeres Image, mehr Angriffsfläche), prüft sich die App mit dem .NET, das ohnehin drin ist.
public static class HealthProbe
{
    public const string Argument = "--healthcheck";

    public static async Task<int> RunAsync()
    {
        // Standard-Port im aspnet-Image ist 8080 (Umgebungsvariable ASPNETCORE_HTTP_PORTS).
        var port = (Environment.GetEnvironmentVariable("ASPNETCORE_HTTP_PORTS") ?? "8080").Split(';', ',')[0];
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(3) };
        try
        {
            using var response = await http.GetAsync($"http://127.0.0.1:{port}/healthz");
            return response.IsSuccessStatusCode ? 0 : 1;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            return 1;
        }
    }
}
