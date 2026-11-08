using System.Net;
using System.Net.Http.Json;
using HomeControl.Api.Data;
using HomeControl.Api.Models;
using HomeControl.Api.Tests.Fakes;
using HomeControl.Api.Tuya;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace HomeControl.Api.Tests;

// Fehlende Berechtigung, offline und fehlende Schaltfunktion müssen unterscheidbar sein.
// Kein echtes Gerät wird geschaltet: Die Tuya-Cloud spielt FakeTuyaHandler.
public class TuyaSwitchEndpointTests
{
    private const string TuyaId = "tuya-stehlampe";

    private sealed class FakeTuyaApiFactory : HomeControlApiFactory
    {
        public FakeTuyaHandler Tuya { get; } = new();

        protected override Dictionary<string, string?> Settings => new()
        {
            ["Tuya:ClientId"] = "test-client",
            ["Tuya:ClientSecret"] = "test-secret",
        };

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.ConfigureTestServices(services =>
                services.AddHttpClient<TuyaClient>().ConfigurePrimaryHttpMessageHandler(() => Tuya));
        }
    }

    private static async Task<(HttpClient Client, Guid DeviceId)> PrepareAsync(FakeTuyaApiFactory factory)
    {
        var tokens = await factory.GetTokensAsync();
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var room = new Room { Name = "Wohnzimmer" };
        var device = new Device { Name = "Stehlampe", Type = "Light", RoomId = room.Id, TuyaDeviceId = TuyaId, LastValue = "AUS" };
        db.Rooms.Add(room);
        db.Devices.Add(device);
        await db.SaveChangesAsync();
        return (factory.CreateClientWithToken(tokens.AdminToken), device.Id);
    }

    private static Task<HttpResponseMessage> SwitchOnAsync(HttpClient client, Guid id) =>
        client.PostAsJsonAsync($"/api/tuya/devices/{id}/switch", new { on = true });

    private static async Task AssertRefusedAsync(HttpResponseMessage response, FakeTuyaHandler tuya, HttpStatusCode code, string text)
    {
        Assert.Equal(code, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.Contains(text, problem!.Title);
        Assert.Empty(tuya.CommandBodies);
    }

    [Fact]
    public async Task Geraet_ausserhalb_des_Projekts_meldet_fehlende_Berechtigung()
    {
        using var factory = new FakeTuyaApiFactory();
        factory.Tuya.DeviceListResponse = _ => TuyaJson.DeviceList(false, null, ("anderes-geraet", "Heizdecke", true));
        var (client, id) = await PrepareAsync(factory);

        await AssertRefusedAsync(await SwitchOnAsync(client, id), factory.Tuya, HttpStatusCode.Conflict, "Keine Berechtigung");
    }

    [Fact]
    public async Task Offline_Geraet_wird_als_offline_gemeldet()
    {
        using var factory = new FakeTuyaApiFactory();
        factory.Tuya.DeviceListResponse = _ => TuyaJson.DeviceList(false, null, (TuyaId, "Stehlampe", false));
        var (client, id) = await PrepareAsync(factory);

        await AssertRefusedAsync(await SwitchOnAsync(client, id), factory.Tuya, HttpStatusCode.Conflict, "offline");
    }

    [Fact]
    public async Task Tuya_Code_1106_wird_als_Berechtigung_gemeldet()
    {
        using var factory = new FakeTuyaApiFactory();
        factory.Tuya.DeviceListResponse = _ => TuyaJson.DeviceList(false, null, (TuyaId, "Stehlampe", true));
        factory.Tuya.StatusResponse = (_, _) => TuyaJson.Error("1106", "permission deny");
        var (client, id) = await PrepareAsync(factory);

        await AssertRefusedAsync(await SwitchOnAsync(client, id), factory.Tuya, HttpStatusCode.Conflict, "1106");
    }

    [Fact]
    public async Task Ohne_switch_1_wird_fehlende_Schaltfunktion_gemeldet()
    {
        using var factory = new FakeTuyaApiFactory();
        factory.Tuya.DeviceListResponse = _ => TuyaJson.DeviceList(false, null, (TuyaId, "Stehlampe", true));
        factory.Tuya.StatusResponse = (_, _) => """{ "success": true, "result": [ { "code": "countdown_1", "value": 0 } ] }""";
        var (client, id) = await PrepareAsync(factory);

        await AssertRefusedAsync(await SwitchOnAsync(client, id), factory.Tuya, HttpStatusCode.UnprocessableEntity, "switch_1");
    }

    [Fact]
    public async Task Online_Geraet_gilt_erst_nach_gelesenem_Zustand_als_geschaltet()
    {
        using var factory = new FakeTuyaApiFactory();
        factory.Tuya.DeviceListResponse = _ => TuyaJson.DeviceList(false, null, (TuyaId, "Stehlampe", true));
        // Erster Abruf vor dem Befehl: aus. Danach bestätigt Tuya: ein.
        factory.Tuya.StatusResponse = (number, _) => TuyaJson.Status(number > 1);
        var (client, id) = await PrepareAsync(factory);

        var response = await SwitchOnAsync(client, id);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Single(factory.Tuya.CommandBodies);
    }

    [Fact]
    public async Task Ohne_gelesene_Bestaetigung_gilt_der_Befehl_nicht_als_erfolgreich()
    {
        using var factory = new FakeTuyaApiFactory();
        factory.Tuya.DeviceListResponse = _ => TuyaJson.DeviceList(false, null, (TuyaId, "Stehlampe", true));
        var (client, id) = await PrepareAsync(factory);

        var response = await SwitchOnAsync(client, id);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Single(factory.Tuya.CommandBodies);
        using var scope = factory.Services.CreateScope();
        var device = await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().Devices.FindAsync(id);
        Assert.Equal("AUS", device!.LastValue);
    }
}
