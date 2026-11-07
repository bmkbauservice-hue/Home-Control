using System.Text.Json;
using HomeControl.Api.Auth;
using HomeControl.Api.Demo;
using HomeControl.Api.RateLimiting;
using HomeControl.Api.Tuya;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace HomeControl.Api.Scale;

public record ScaleField(string Code, string Label, JsonElement RawValue,
    string? Unit, int? Scale, DateTimeOffset? ReportedAtUtc);
public record ScaleReading(ScaleBinding? Binding, List<ScaleField> Fields,
    DateTimeOffset? RetrievedAtUtc, string Note);
public record ScaleSelection(string? TuyaId, string? WeightCode);

public sealed class ScaleReadGate
{
    public SemaphoreSlim Gate { get; } = new(1, 1);
}

public static class ScaleEndpoints
{
    public static void MapScaleEndpoints(this WebApplication app)
    {
        // Körperwerte bleiben ausschließlich für Admins sichtbar, auch bei offenem Lesemodus.
        var scale = app.MapGroup("/api/scale").WithTags("Waage")
            .RequireAuthorization(AuthPolicies.AdminOnly).RequireRateLimiting(RateLimitPolicies.Tuya);
        scale.MapGet("", Read);
        scale.MapPut("", Select);
    }

    private static async Task<IResult> Read(HttpContext http, IConfiguration config, ScaleSettingsStore store,
        TuyaClient client, IOptionsMonitor<TuyaOptions> options, IMemoryCache cache, ScaleReadGate readGate,
        TimeProvider clock, CancellationToken ct)
    {
        http.Response.Headers.CacheControl = "no-store";
        if (DemoMode.IsEnabled(config))
            return Results.Ok(new ScaleReading(null, [], null, "In der Demo werden keine persönlichen Waagendaten geladen."));
        var binding = await store.ReadAsync(ct);
        if (binding is null)
            return Results.Ok(new ScaleReading(null, [], null,
                "Noch keine Waage verbunden. Ist sie nur per Bluetooth in der Tuya-App sichtbar, stehen ihre Werte möglicherweise nicht in der Cloud bereit."));
        if (options.CurrentValue.GetMissingCredentials().Count > 0)
            return Results.Ok(new ScaleReading(binding, [], null, "Tuya-Zugang im Backend noch nicht eingerichtet."));

        await readGate.Gate.WaitAsync(ct);
        try
        {
            var cacheKey = "scale-readings:" + binding.TuyaId;
            if (cache.TryGetValue<ScaleReading>(cacheKey, out var saved) && saved is not null)
                return Results.Ok(saved with { Binding = binding });

            var properties = await client.GetReportedPropertiesAsync(binding.TuyaId, ct);
            TuyaDeviceSpecification? specification = null;
            try { specification = await client.GetDeviceSpecificationAsync(binding.TuyaId, ct); }
            catch (Exception ex) when (ex is TuyaApiException or HttpRequestException or JsonException
                                       || ex is OperationCanceledException && !ct.IsCancellationRequested)
            {
                // Ohne Felddefinition bleiben Werte ausdrücklich Rohwerte, keine geratenen Einheiten.
            }
            var fields = properties.Select(property => ToField(property,
                specification?.Status.FirstOrDefault(definition => definition.Code == property.Code))).ToList();
            var result = new ScaleReading(binding, fields, clock.GetUtcNow(),
                "Zuletzt an Tuya gemeldete Cloud-Werte. Die Meldezeit eines Felds ist keine bestätigte Wiegezeit. Abrufe werden 60 Sekunden zwischengespeichert.");
            cache.Set(cacheKey, result, TimeSpan.FromSeconds(60));
            return Results.Ok(result);
        }
        catch (Exception ex) when (ex is TuyaApiException or HttpRequestException or JsonException
                                   || ex is OperationCanceledException && !ct.IsCancellationRequested)
        {
            return Results.Problem(statusCode: 502,
                title: "Tuya liefert für diese Waage derzeit keine lesbaren Cloud-Eigenschaften. Verbindung und API-Freigabe prüfen.");
        }
        finally { readGate.Gate.Release(); }
    }

    private static async Task<IResult> Select(ScaleSelection input, HttpContext http, IConfiguration config,
        ScaleSettingsStore store, TuyaClient client, IOptionsMonitor<TuyaOptions> options, CancellationToken ct)
    {
        http.Response.Headers.CacheControl = "no-store";
        if (DemoMode.IsEnabled(config))
            return Results.Problem(statusCode: 409, title: "In der Demo wird keine echte Waage verbunden.");
        if (input.TuyaId is null)
        {
            await store.WriteAsync(null, ct);
            return Results.NoContent();
        }
        if (string.IsNullOrWhiteSpace(input.TuyaId) || input.TuyaId.Length > 256 || input.WeightCode?.Length > 128)
            return Results.BadRequest(new { title = "Ungültige Geräteauswahl." });
        if (options.CurrentValue.GetMissingCredentials().Count > 0)
            return Results.Problem(statusCode: 503, title: "Tuya-Zugang noch nicht eingerichtet.");
        try
        {
            var devices = await client.GetLinkedAppDevicesAsync(ct);
            var device = devices.FirstOrDefault(d => d.Id == input.TuyaId);
            if (device is null)
                return Results.BadRequest(new { title = "Dieses Gerät ist nicht mit dem Tuya-Cloud-Projekt verbunden." });
            var code = string.IsNullOrWhiteSpace(input.WeightCode) ? null : input.WeightCode;
            if (code is not null)
            {
                var properties = await client.GetReportedPropertiesAsync(device.Id, ct);
                if (!properties.Any(p => p.Code == code && p.Value.ValueKind == JsonValueKind.Number))
                    return Results.BadRequest(new { title = "Der gewählte Gewichtsdatenpunkt wurde nicht als Zahl gemeldet." });
            }
            await store.WriteAsync(new ScaleBinding(device.Id, device.Name, code), ct);
            return Results.NoContent();
        }
        catch (Exception ex) when (ex is TuyaApiException or HttpRequestException or JsonException
                                   || ex is OperationCanceledException && !ct.IsCancellationRequested)
        {
            return Results.Problem(statusCode: 502, title: "Tuya-Geräteauswahl derzeit nicht prüfbar.");
        }
    }

    public static ScaleField ToField(TuyaReportedProperty property, TuyaDataPointDefinition? definition)
    {
        string? unit = null;
        int? scale = null;
        if (definition is not null)
        {
            try
            {
                using var metadata = JsonDocument.Parse(definition.Values);
                if (metadata.RootElement.ValueKind == JsonValueKind.Object)
                {
                    if (metadata.RootElement.TryGetProperty("unit", out var value) && value.ValueKind == JsonValueKind.String)
                        unit = value.GetString();
                    if (metadata.RootElement.TryGetProperty("scale", out var factor) && factor.ValueKind == JsonValueKind.Number
                        && factor.TryGetInt32(out var number) && number is >= 0 and <= 9)
                        scale = number;
                }
            }
            catch (JsonException) { }
        }
        return new ScaleField(property.Code, string.IsNullOrWhiteSpace(definition?.Name) ? property.Code : definition.Name,
            property.Value, unit, scale, property.ReportedAtUtc);
    }
}
