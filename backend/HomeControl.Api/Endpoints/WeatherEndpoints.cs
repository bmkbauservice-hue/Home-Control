using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.Caching.Memory;

namespace HomeControl.Api.Endpoints;

// Wetterdaten vom Wetterdienst, nicht von einem Sensor im Haus.
public static class WeatherEndpoints
{
    private const string CacheKey = "weather-current";

    public static void MapWeatherEndpoints(this WebApplication app)
    {
        app.MapGet("/api/weather/current", GetCurrent).WithTags("Wetter");
    }

    private static async Task<IResult> GetCurrent(
        IHttpClientFactory clients, IConfiguration config, IMemoryCache cache,
        ILoggerFactory loggerFactory, CancellationToken cancellationToken)
    {
        if (cache.TryGetValue(CacheKey, out CurrentWeather? saved) && saved is not null)
        {
            return Results.Ok(saved);
        }

        var logger = loggerFactory.CreateLogger("HomeControl.Wetter");
        var latitude = config.GetValue<double?>("Weather:Latitude") ?? 52.753;
        var longitude = config.GetValue<double?>("Weather:Longitude") ?? 13.236;
        var location = config["Weather:Location"] ?? "Oranienburg";

        if (latitude is < -90 or > 90 || longitude is < -180 or > 180)
        {
            return Results.Problem(statusCode: 503, title: "Der Wetterstandort ist ungültig konfiguriert.");
        }

        var lat = latitude.ToString(CultureInfo.InvariantCulture);
        var lon = longitude.ToString(CultureInfo.InvariantCulture);
        var url = $"https://api.open-meteo.com/v1/forecast?latitude={lat}&longitude={lon}" +
                  "&current=temperature_2m,weather_code,is_day&timezone=Europe%2FBerlin";

        try
        {
            using var response = await clients.CreateClient("weather").GetAsync(url, cancellationToken);
            response.EnsureSuccessStatusCode();
            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var json = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
            var current = json.RootElement.GetProperty("current");

            var weather = new CurrentWeather(
                location,
                current.GetProperty("temperature_2m").GetDouble(),
                current.GetProperty("weather_code").GetInt32(),
                current.GetProperty("is_day").GetInt32() == 1,
                current.GetProperty("time").GetString() ?? "",
                DateTimeOffset.UtcNow,
                "Open-Meteo");

            // Keine veralteten Fantasiewerte: nur erfolgreiche Antworten zwischenspeichern.
            cache.Set(CacheKey, weather, TimeSpan.FromMinutes(5));
            return Results.Ok(weather);
        }
        catch (HttpRequestException ex)
        {
            logger.LogWarning(ex, "Wetterdienst nicht erreichbar.");
            return Results.Problem(statusCode: 502, title: "Live-Wetter derzeit nicht erreichbar.");
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return Results.Problem(statusCode: 504, title: "Der Wetterdienst antwortet nicht.");
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException)
        {
            logger.LogWarning(ex, "Wetterdienst hat unerwartete Daten geliefert.");
            return Results.Problem(statusCode: 502, title: "Wetterdaten konnten nicht gelesen werden.");
        }
    }

    public record CurrentWeather(
        string Location,
        double TemperatureC,
        int WeatherCode,
        bool IsDay,
        string ObservationTime,
        DateTimeOffset RetrievedAtUtc,
        string Source);
}
