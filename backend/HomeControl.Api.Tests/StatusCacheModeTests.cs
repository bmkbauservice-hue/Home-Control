using System.Net.Http.Json;
using HomeControl.Api.Contracts;
using HomeControl.Api.Status;
using HomeControl.Api.Tests.Fakes;
using HomeControl.Api.Tuya;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace HomeControl.Api.Tests;

// GET /api/status und das Tuya-Kontingent: Läuft der Hintergrund-Abgleich, liest der Endpunkt nur den
// Speicher und fragt Tuya NIE selbst. Ohne Hintergrund-Abgleich fragt er selbst, höchstens alle 5 Minuten.
// Die Tuya-Cloud spielt FakeTuyaHandler, so lässt sich jeder Aufruf zählen.
public class StatusCacheModeTests
{
    private sealed class FakeTuyaApiFactory : HomeControlApiFactory
    {
        public FakeTuyaHandler Tuya { get; } = new();

        // Erfundene Zugangsdaten: Tuya gilt als eingerichtet, Anfragen landen aber beim FakeTuyaHandler.
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

        public TuyaOnlineCache Cache => Services.GetRequiredService<TuyaOnlineCache>();
    }

    private static async Task<StatusResponse> GetStatusAsync(HttpClient client) =>
        (await client.GetFromJsonAsync<StatusResponse>("/api/status"))!;

    [Fact]
    public async Task Mit_Hintergrund_Abgleich_vor_dem_ersten_Durchlauf_unbekannt_und_kein_Aufruf()
    {
        using var factory = new FakeTuyaApiFactory();
        using var client = factory.CreateClient();
        factory.Cache.BackgroundRefreshActive = true;

        var status = await GetStatusAsync(client);

        Assert.Equal(OnlineStates.Unknown, status.Tuya);
        Assert.Null(status.TuyaCheckedAtUtc);
        Assert.Empty(factory.Tuya.DeviceListPaths);
        Assert.Equal(0, factory.Tuya.TokenRequestCount);
    }

    [Fact]
    public async Task Mit_Hintergrund_Abgleich_liest_der_Status_nur_den_Speicher()
    {
        using var factory = new FakeTuyaApiFactory();
        using var client = factory.CreateClient();
        factory.Cache.BackgroundRefreshActive = true;
        // So, als hätte der Hintergrund-Abgleich gerade die Liste geholt.
        await factory.Cache.RefreshAsync(
            _ => Task.FromResult(new List<TuyaCloudDevice> { new("tuya-lampe", "Lampe", "Lampe", "dj", true) }),
            CancellationToken.None);

        for (var i = 0; i < 10; i++)
        {
            var status = await GetStatusAsync(client);
            Assert.Equal(OnlineStates.Online, status.Tuya);
            Assert.NotNull(status.TuyaCheckedAtUtc);
        }

        // Zehn Abfragen der LEDs, kein einziger Aufruf bei Tuya.
        Assert.Empty(factory.Tuya.DeviceListPaths);
    }

    [Fact]
    public async Task Ohne_Hintergrund_Abgleich_fragt_der_Status_selbst_hoechstens_einmal_in_5_Minuten()
    {
        using var factory = new FakeTuyaApiFactory();
        using var client = factory.CreateClient();
        Assert.False(factory.Cache.BackgroundRefreshActive);

        for (var i = 0; i < 10; i++)
        {
            var status = await GetStatusAsync(client);
            Assert.Equal(OnlineStates.Online, status.Tuya);
        }

        Assert.Single(factory.Tuya.DeviceListPaths);
    }
}
