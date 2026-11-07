using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using HomeControl.Api.Scale;
using HomeControl.Api.Tests.Fakes;
using HomeControl.Api.Tuya;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace HomeControl.Api.Tests;

public class ScaleEndpointTests
{
    [Fact]
    public async Task Koerperwerte_bleiben_auch_bei_offenem_Lesemodus_nur_fuer_Admins()
    {
        using var factory = new ScaleFactory();
        using var anonymous = factory.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/scale")).StatusCode);
        var (_, token) = await factory.GetTokensAsync();
        using var user = factory.CreateClientWithToken(token);
        Assert.Equal(HttpStatusCode.Forbidden, (await user.GetAsync("/api/scale")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await user.PutAsJsonAsync("/api/scale", new ScaleSelection("scale1", null))).StatusCode);
        Assert.Equal(0, factory.Handler.Requests);
    }

    [Fact]
    public async Task Ohne_Zuordnung_keine_erfundenen_Messwerte_und_keine_Cloud_Anfrage()
    {
        using var factory = new ScaleFactory();
        var (token, _) = await factory.GetTokensAsync();
        using var admin = factory.CreateClientWithToken(token);
        var response = await admin.GetAsync("/api/scale");
        var reading = await response.Content.ReadFromJsonAsync<ScaleReading>();
        Assert.Null(reading!.Binding);
        Assert.Null(reading.RetrievedAtUtc);
        Assert.Empty(reading.Fields);
        Assert.Equal("no-store", response.Headers.CacheControl!.ToString());
        Assert.Equal(0, factory.Handler.Requests);
    }

    [Fact]
    public async Task Offline_Waage_erhaelt_letzte_Werte_und_Feldzeiten_ohne_Schaltbefehl()
    {
        using var factory = new ScaleFactory();
        var (token, _) = await factory.GetTokensAsync();
        using var admin = factory.CreateClientWithToken(token);
        var selected = await admin.PutAsJsonAsync("/api/scale", new ScaleSelection("scale1", "weight"));
        Assert.Equal(HttpStatusCode.NoContent, selected.StatusCode);
        var reading = await admin.GetFromJsonAsync<ScaleReading>("/api/scale");
        Assert.Equal("weight", reading!.Binding!.WeightCode);
        var weight = Assert.Single(reading.Fields, field => field.Code == "weight");
        var battery = Assert.Single(reading.Fields, field => field.Code == "battery");
        Assert.Equal(7234, weight.RawValue.GetInt32());
        Assert.Equal("kg", weight.Unit);
        Assert.Equal(2, weight.Scale);
        Assert.Equal(DateTimeOffset.FromUnixTimeMilliseconds(1700000000000), weight.ReportedAtUtc);
        Assert.True(battery.ReportedAtUtc > weight.ReportedAtUtc);
        Assert.Null(Assert.Single(reading.Fields, field => field.Code == "unknown").ReportedAtUtc);
        var calls = factory.Handler.Requests;
        await admin.GetAsync("/api/scale");
        Assert.Equal(calls, factory.Handler.Requests);
        Assert.All(factory.Handler.Methods, method => Assert.Equal(HttpMethod.Get, method));
        Assert.DoesNotContain("7234", await File.ReadAllTextAsync(factory.SettingsFile));
    }

    [Fact]
    public async Task Fremdes_Geraet_und_unbekannter_Gewichtscode_werden_nicht_gespeichert()
    {
        using var factory = new ScaleFactory();
        var (token, _) = await factory.GetTokensAsync();
        using var admin = factory.CreateClientWithToken(token);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PutAsJsonAsync("/api/scale", new ScaleSelection("foreign", null))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PutAsJsonAsync("/api/scale", new ScaleSelection("scale1", "guessed_code"))).StatusCode);
        Assert.False(File.Exists(factory.SettingsFile));
    }

    [Fact]
    public async Task Fehlende_Felddefinition_laesst_Rohwerte_ohne_geratene_Einheit()
    {
        using var factory = new ScaleFactory();
        factory.Handler.SpecificationAllowed = false;
        var (token, _) = await factory.GetTokensAsync();
        using var admin = factory.CreateClientWithToken(token);
        await admin.PutAsJsonAsync("/api/scale", new ScaleSelection("scale1", null));
        var reading = await admin.GetFromJsonAsync<ScaleReading>("/api/scale");
        var weight = Assert.Single(reading!.Fields, field => field.Code == "weight");
        Assert.Equal(7234, weight.RawValue.GetInt32());
        Assert.Null(weight.Unit);
        Assert.Null(weight.Scale);
    }

    [Fact]
    public async Task Demo_liest_oder_ueberschreibt_keine_private_Zuordnung()
    {
        using var factory = new ScaleFactory();
        var (token, _) = await factory.GetTokensAsync();
        using var admin = factory.CreateClientWithToken(token);
        await File.WriteAllTextAsync(factory.SettingsFile, "Absichtlich keine lesbare private Zuordnung");
        factory.Services.GetRequiredService<IConfiguration>()["HomeControl:DemoMode"] = "true";
        var reading = await admin.GetFromJsonAsync<ScaleReading>("/api/scale");
        Assert.Null(reading!.Binding);
        Assert.Empty(reading.Fields);
        Assert.Equal(HttpStatusCode.Conflict, (await admin.PutAsJsonAsync("/api/scale", new ScaleSelection(null, null))).StatusCode);
        Assert.Equal("Absichtlich keine lesbare private Zuordnung", await File.ReadAllTextAsync(factory.SettingsFile));
        Assert.Equal(0, factory.Handler.Requests);
    }

    [Fact]
    public async Task Zuordnung_ueberlebt_neuen_Store_ohne_Koerperwerte_zu_speichern()
    {
        var file = Path.Combine(Path.GetTempPath(), "hc-scale-" + Guid.NewGuid() + ".json");
        try
        {
            var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Scale:SettingsPath"] = file }).Build();
            var binding = new ScaleBinding("scale1", "Testwaage", "weight");
            await new ScaleSettingsStore(config).WriteAsync(binding, CancellationToken.None);
            Assert.Equal(binding, await new ScaleSettingsStore(config).ReadAsync(CancellationToken.None));
            if (!OperatingSystem.IsWindows())
                Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(file));
        }
        finally { File.Delete(file); }
    }

    [Theory]
    [InlineData("{\"scale\":\"unknown\"}")]
    [InlineData("{\"scale\":-1}")]
    [InlineData("{\"scale\":100}")]
    [InlineData("kein JSON")]
    public void Unbrauchbare_Metadaten_erfinden_keine_Skalierung(string metadata)
    {
        var property = new TuyaReportedProperty("weight", JsonSerializer.SerializeToElement(7234), null);
        var field = ScaleEndpoints.ToField(property, new TuyaDataPointDefinition("weight", "Gewicht", "Integer", metadata));
        Assert.Null(field.Scale);
        Assert.Equal(7234, field.RawValue.GetInt32());
    }

    private sealed class ScaleFactory : HomeControlApiFactory
    {
        public string SettingsFile { get; } = Path.Combine(Path.GetTempPath(), "hc-scale-" + Guid.NewGuid() + ".json");
        public ScaleHandler Handler { get; } = new();
        protected override Dictionary<string, string?> Settings => new()
        {
            ["Scale:SettingsPath"] = SettingsFile,
            ["Tuya:ClientId"] = "test",
            ["Tuya:ClientSecret"] = "test",
        };
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.ConfigureTestServices(services => services.AddHttpClient<TuyaClient>()
                .ConfigurePrimaryHttpMessageHandler(() => Handler));
        }
        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            File.Delete(SettingsFile);
        }
    }

    private sealed class ScaleHandler : HttpMessageHandler
    {
        public int Requests { get; private set; }
        public List<HttpMethod> Methods { get; } = [];
        public bool SpecificationAllowed { get; set; } = true;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Requests++;
            Methods.Add(request.Method);
            var path = request.RequestUri!.AbsolutePath;
            var result = path switch
            {
                "/v1.0/token" => """{"access_token":"fake","expire_time":7200}""",
                "/v1.0/iot-01/associated-users/devices" => """{"devices":[{"id":"scale1","name":"Testwaage","online":false}],"has_more":false}""",
                "/v2.0/cloud/thing/scale1/shadow/properties" => """{"properties":[{"code":"weight","value":7234,"time":1700000000000},{"code":"battery","value":90,"time":1700000600000},{"code":"unknown","value":"original"}]}""",
                "/v1.0/iot-03/devices/scale1/specification" when SpecificationAllowed => """{"status":[{"code":"weight","name":"Gewicht","type":"Integer","values":{"unit":"kg","scale":2}}]}""",
                _ => null,
            };
            var body = result is null ? """{"success":false,"code":"1106","msg":"denied"}""" : "{\"success\":true,\"result\":" + result + "}";
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") });
        }
    }
}
