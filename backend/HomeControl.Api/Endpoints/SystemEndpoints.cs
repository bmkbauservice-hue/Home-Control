using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using HomeControl.Api.Auth;
using HomeControl.Api.Contracts;
using HomeControl.Api.Data;
using HomeControl.Api.Demo;
using HomeControl.Api.Tuya;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace HomeControl.Api.Endpoints;

public static class SystemEndpoints
{
    // GET /api/system/info – Version, Commit, Umgebung, Datenbankgröße. Nur Admin: Das geht Gäste nichts an.
    public static void MapSystemEndpoints(this WebApplication app)
    {
        app.MapGet("/api/system/info", GetInfo)
            .RequireAuthorization(AuthPolicies.AdminOnly)
            .WithTags("Status")
            .WithSummary("Version, Commit, Umgebung, Datenbankgröße (nur Admin)");
    }

    private static async Task<IResult> GetInfo(
        ApplicationDbContext db,
        IHostEnvironment environment,
        IConfiguration configuration,
        IOptionsMonitor<TuyaOptions> tuya,
        CancellationToken cancellationToken)
    {
        // Das .NET-SDK schreibt beim Bauen in einem Git-Repo "1.0.0+<Commit>" in die InformationalVersion.
        // Ohne Git (z. B. im Docker-Build ohne .git) fehlt der Teil nach dem "+".
        var informational = typeof(SystemEndpoints).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "unbekannt";
        var parts = informational.Split('+', 2);
        var commit = parts.Length == 2 ? parts[1][..Math.Min(7, parts[1].Length)] : null;

        // SQLite: Größe der Datei plus Write-Ahead-Log (dort stehen Änderungen, bis SQLite sie übernimmt).
        var dataSource = new SqliteConnectionStringBuilder(db.Database.GetConnectionString()).DataSource;
        long? bytes = File.Exists(dataSource)
            ? new FileInfo(dataSource).Length + (File.Exists(dataSource + "-wal") ? new FileInfo(dataSource + "-wal").Length : 0)
            : null;

        return Results.Ok(new SystemInfoResponse(
            parts[0],
            commit,
            environment.EnvironmentName,
            DemoMode.IsEnabled(configuration),
            RuntimeInformation.FrameworkDescription,
            new DateTimeOffset(Process.GetCurrentProcess().StartTime.ToUniversalTime(), TimeSpan.Zero),
            Path.GetFileName(dataSource),
            bytes,
            tuya.CurrentValue.AutoSyncSeconds,
            await db.Users.CountAsync(cancellationToken),
            await db.Rooms.CountAsync(cancellationToken),
            await db.Devices.CountAsync(cancellationToken),
            await db.DeviceEvents.CountAsync(cancellationToken)));
    }
}
