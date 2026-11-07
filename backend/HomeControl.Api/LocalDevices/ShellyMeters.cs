using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using HomeControl.Api.Auth;
using HomeControl.Api.Demo;

namespace HomeControl.Api.LocalDevices;

public sealed record ShellyMeterReading(int Id, string Name, string State, double? Watt, double? CounterKwh);

public sealed class ShellyMeters(IConfiguration config, IHttpClientFactory http)
{
    private readonly SemaphoreSlim gate = new(1);
    private ShellyMeterReading[] cached = [];
    private DateTimeOffset refreshAt;

    public static void Register(IServiceCollection services)
    {
        services.AddSingleton<ShellyMeters>();
        services.AddHttpClient("local-shelly", client =>
        {
            client.Timeout = TimeSpan.FromSeconds(3);
            client.MaxResponseContentBufferSize = 32_768;
        }).ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
        {
            AllowAutoRedirect = false,
            UseProxy = false,
            UseCookies = false,
        });
    }

    public static void Map(WebApplication app)
    {
        app.MapGet("/api/local-devices/shelly", async (ShellyMeters meters, HttpContext context, CancellationToken ct) =>
        {
            context.Response.Headers.CacheControl = "no-store";
            return await meters.ReadAsync(ct);
        }).RequireAuthorization(AuthPolicies.SignedInUser);
    }

    public async Task<ShellyMeterReading[]> ReadAsync(CancellationToken ct)
    {
        if (DemoMode.IsEnabled(config)) return [];

        await gate.WaitAsync(ct);
        try
        {
            if (DateTimeOffset.UtcNow < refreshAt) return cached;

            // Geräteziele kommen ausschließlich aus der lokalen Serverkonfiguration.
            var targets = config.GetSection("LocalDevices:Shelly").GetChildren().Take(16)
                .Select((entry, index) => new
                {
                    Id = index,
                    Name = entry["Name"]?.Trim() is { Length: > 0 and <= 100 } name ? name : $"Shelly {index + 1}",
                    Ip = PrivateAddress(entry["Ip"]),
                    Channel = int.TryParse(entry["Channel"] ?? "0", out var channel) && channel is >= 0 and <= 15
                        ? channel : -1,
                }).ToArray();

            using var client = http.CreateClient("local-shelly");
            var seen = new HashSet<(IPAddress, int)>();
            cached = await Task.WhenAll(targets.Select(target =>
                target.Ip is { } ip && target.Channel >= 0 && seen.Add((ip, target.Channel))
                    ? ReadMeterAsync(client, ip, target.Channel, target.Id, target.Name, ct)
                    : Task.FromResult(new ShellyMeterReading(target.Id, target.Name, "configuration", null, null))));
            refreshAt = DateTimeOffset.UtcNow.AddSeconds(30);
            return cached;
        }
        finally
        {
            gate.Release();
        }
    }

    private static IPAddress? PrivateAddress(string? value)
    {
        if (!IPAddress.TryParse(value, out var ip) || ip.AddressFamily != AddressFamily.InterNetwork ||
            ip.ToString() != value)
            return null;

        var bytes = ip.GetAddressBytes();
        return bytes[0] == 10 || bytes[0] == 172 && bytes[1] is >= 16 and <= 31 ||
            bytes[0] == 192 && bytes[1] == 168 ? ip : null;
    }

    private static async Task<ShellyMeterReading> ReadMeterAsync(
        HttpClient client, IPAddress ip, int channel, int id, string name, CancellationToken ct)
    {
        try
        {
            using var response = await client.GetAsync($"http://{ip}/rpc/Switch.GetStatus?id={channel}", ct);
            if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
                return new(id, name, "auth", null, null);
            if (response.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.NotFound)
                return new(id, name, "unsupported", null, null);
            response.EnsureSuccessStatusCode();
            using var doc = JsonDocument.Parse(await response.Content.ReadAsByteArrayAsync(ct));
            var body = doc.RootElement;
            if (body.ValueKind != JsonValueKind.Object || !body.TryGetProperty("id", out var replyId) ||
                replyId.ValueKind != JsonValueKind.Number || !replyId.TryGetInt32(out var replyChannel) || replyChannel != channel)
                return new(id, name, "unsupported", null, null);

            var watt = Number(body, "apower");
            var counter = body.TryGetProperty("aenergy", out var energy) ? Number(energy, "total") / 1000 : null;
            return new(id, name, "online", watt, counter);
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException ||
            ex is OperationCanceledException && !ct.IsCancellationRequested)
        {
            return new(id, name, "unavailable", null, null);
        }
    }

    private static double? Number(JsonElement element, string property) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(property, out var value) &&
        value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out var number) &&
        double.IsFinite(number) && number >= 0 ? number : null;
}
