using System.Net;
using System.Net.Http.Json;
using HomeControl.Api.Blink;
using HomeControl.Api.Tests.Fakes;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace HomeControl.Api.Tests;

// GET /api/blink/cameras/{id}/thumbnail: nur mit JWT, nie mit dem Agenten-Schlüssel, Bild wird
// durchgereicht, Fehler von Blink werden zu verständlichen Meldungen. Blink spielt FakeBlinkCameraClient.
public class BlinkThumbnailEndpointTests(BlinkThumbnailEndpointTests.ThumbnailApiFactory factory)
    : IClassFixture<BlinkThumbnailEndpointTests.ThumbnailApiFactory>
{
    private static readonly DateTimeOffset TakenAt = new(2026, 11, 2, 14, 11, 37, TimeSpan.Zero);

    // Leseschutz bewusst AUS und ein Agenten-Schlüssel gesetzt: Der Bild-Endpunkt muss trotzdem eine
    // Anmeldung per JWT verlangen und den Schlüssel ablehnen.
    public class ThumbnailApiFactory : HomeControlApiFactory
    {
        public const string AgentApiKey = "test-agent-schluessel-nur-fuer-tests-0123456789";

        public FakeBlinkCameraClient Blink { get; } = new();

        protected override Dictionary<string, string?> Settings => new()
        {
            ["Auth:RequireLoginForReading"] = "false",
            ["Agent:ApiKey"] = AgentApiKey,
        };

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.ConfigureTestServices(services =>
                services.AddSingleton<IBlinkCameraClient>(Blink));
        }
    }

    private const string Url = "/api/blink/cameras/1/thumbnail";

    [Fact]
    public async Task Ohne_Anmeldung_401()
    {
        using var client = factory.CreateClient();

        var response = await client.GetAsync(Url);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Mit_Agenten_Schluessel_401()
    {
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", ThumbnailApiFactory.AgentApiKey);

        var response = await client.GetAsync(Url);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Mit_Anmeldung_wird_das_Bild_durchgereicht()
    {
        factory.Blink.Thumbnail = _ => new BlinkThumbnail(BlinkJson.FakeJpeg, "image/jpeg", TakenAt);
        var (_, userToken) = await factory.GetTokensAsync();
        using var client = factory.CreateClientWithToken(userToken);

        var response = await client.GetAsync(Url);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("image/jpeg", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal(BlinkJson.FakeJpeg, await response.Content.ReadAsByteArrayAsync());
        Assert.True(response.Headers.CacheControl?.NoStore);
        Assert.Equal(TakenAt, DateTimeOffset.Parse(response.Headers.GetValues("X-Thumbnail-Taken-At").Single()));
    }

    [Fact]
    public async Task Fehler_von_Blink_ergibt_verstaendliche_Meldung_statt_500()
    {
        factory.Blink.Thumbnail = _ => throw new BlinkApiException("Blink hat das Vorschaubild nicht geliefert (HTTP 500).");
        var (_, userToken) = await factory.GetTokensAsync();
        using var client = factory.CreateClientWithToken(userToken);

        var response = await client.GetAsync(Url);

        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.Equal("Blink hat die Anfrage abgelehnt.", problem!.Title);
    }

    [Fact]
    public async Task Kein_Bild_ergibt_404()
    {
        factory.Blink.Thumbnail = _ => null;
        var (_, userToken) = await factory.GetTokensAsync();
        using var client = factory.CreateClientWithToken(userToken);

        var response = await client.GetAsync(Url);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
