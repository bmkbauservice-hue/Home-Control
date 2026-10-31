using System.Globalization;
using HomeControl.Api.Contracts;
using HomeControl.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace HomeControl.Api.Endpoints;

public static class ReportEndpoints
{
    // So viele Geräte zeigt die Rangliste höchstens.
    private const int TopDeviceLimit = 5;

    // Registriert alle Endpunkte unter /api/reports.
    public static void MapReportEndpoints(this WebApplication app)
    {
        var reports = app.MapGroup("/api/reports").WithTags("Reports");

        reports.MapGet("/daily", GetDailyReport);
    }

    // GET /api/reports/daily?date=YYYY-MM-DD – Auswertung für einen UTC-Tag
    // Das Datum kommt bewusst als Text an, damit wir es selbst streng prüfen
    // und eine verständliche Fehlermeldung zurückgeben können.
    private static async Task<IResult> GetDailyReport(string? date, ApplicationDbContext db)
    {
        if (string.IsNullOrEmpty(date))
        {
            return DateError("Bitte ein Datum im Format JJJJ-MM-TT angeben.");
        }

        // Genau dieses Format, keine Leerzeichen, keine Uhrzeit, nur gültige Kalendertage.
        if (!DateOnly.TryParseExact(date, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var day))
        {
            return DateError($"„{date}“ ist kein gültiges Datum im Format JJJJ-MM-TT.");
        }

        // Der UTC-Tag: von 00:00 UTC (einschließlich) bis 00:00 UTC am Folgetag (ausschließlich).
        var start = new DateTimeOffset(day.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
        var end = start.AddDays(1);

        var dayEvents = db.DeviceEvents.Where(e => e.Timestamp >= start && e.Timestamp < end);

        var currentDeviceCount = await db.Devices.CountAsync();
        var eventCount = await dayEvents.CountAsync();

        // Aktionen mit Anzahl, die häufigste zuerst.
        var actions = await dayEvents
            .GroupBy(e => e.Action)
            .Select(g => new { Action = g.Key, Count = g.Count() })
            .OrderByDescending(a => a.Count)
            .ThenBy(a => a.Action)
            .ToListAsync();

        // Geräte mit den meisten Ereignissen. Geräte ohne Ereignisse tauchen in der
        // Gruppierung gar nicht auf. Bei Gleichstand: nach Name, dann nach Id.
        var topDevices = await dayEvents
            .GroupBy(e => new { e.DeviceId, e.Device.Name })
            .Select(g => new { g.Key.DeviceId, g.Key.Name, Count = g.Count() })
            .OrderByDescending(d => d.Count)
            .ThenBy(d => d.Name)
            .ThenBy(d => d.DeviceId)
            .Take(TopDeviceLimit)
            .ToListAsync();

        var report = new DailyReportResponse(
            day,
            currentDeviceCount,
            eventCount,
            actions.Select(a => new ActionCount(a.Action, a.Count)).ToList(),
            topDevices.Select(d => new DeviceEventCount(d.DeviceId, d.Name, d.Count)).ToList());

        return Results.Ok(report);
    }

    // 400 als ProblemDetails mit einer Meldung für das Feld "date".
    private static IResult DateError(string message)
    {
        return Results.ValidationProblem(
            new Dictionary<string, string[]> { ["date"] = [message] },
            title: "Ungültiges Datum.");
    }
}
