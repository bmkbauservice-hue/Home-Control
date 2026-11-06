using System.Net;
using System.Text;
using System.Text.Json;
using HomeControl.Api.Tests.Fakes;
using HomeControl.Api.Tuya;

namespace HomeControl.Api.Tests;

public class TuyaDiagnosticReadTests
{
    private static TuyaClient CreateClient(DiagnosticHandler handler)
    {
        var time = new FakeTimeProvider();
        var options = new TestOptionsMonitor<TuyaOptions>(new TuyaOptions { ClientId = "test", ClientSecret = "test" });
        return new TuyaClient(new HttpClient(handler) { BaseAddress = new Uri("https://openapi.tuyaeu.com") },
            options, new TuyaTokenStore(time), time);
    }

    [Fact]
    public async Task Geraeteinformation_GibtKeineSchluesselOderNetzwerkdatenWeiter()
    {
        var handler = new DiagnosticHandler
        {
            Result = """{"id":"device1","name":"Licht","online":true,"local_key":"private-key","ip":"private-address","lat":"private-location"}"""
        };
        var device = await CreateClient(handler).GetDeviceInformationAsync("device1", CancellationToken.None);
        Assert.True(device.Online);
        Assert.DoesNotContain("private-", JsonSerializer.Serialize(device));
        Assert.Equal("/v1.1/iot-03/devices/device1", handler.Paths.Last());
        Assert.All(handler.Methods, method => Assert.Equal(HttpMethod.Get, method));
    }

    [Fact]
    public async Task Geraeteinformation_VerarbeitetAuchDieDokumentierteInnereAntwort()
    {
        var handler = new DiagnosticHandler { Result = """{"result":{"id":"device1","online":false,"local_key":"private-key"}}""" };
        var device = await CreateClient(handler).GetDeviceInformationAsync("device1", CancellationToken.None);
        Assert.False(device.Online);
        Assert.DoesNotContain("private-key", JsonSerializer.Serialize(device));
    }

    [Fact]
    public async Task Spezifikation_ErhaeltEinheitenUndSkalierungOhneWerteZuErfinden()
    {
        var handler = new DiagnosticHandler
        {
            Result = """{"category":"example","functions":[{"code":"switch_1","type":"Boolean","values":"{}"}],"status":[{"code":"weight","name":"Gewicht","type":"Integer","values":"{\"unit\":\"kg\",\"scale\":2}"}]}"""
        };
        var spec = await CreateClient(handler).GetDeviceSpecificationAsync("device1", CancellationToken.None);
        Assert.Equal("switch_1", Assert.Single(spec.Functions).Code);
        var definition = Assert.Single(spec.Status);
        Assert.Equal("{\"unit\":\"kg\",\"scale\":2}", definition.Values);
        Assert.Equal("Integer", definition.Type);
        Assert.Equal("/v1.0/iot-03/devices/device1/specification", handler.Paths.Last());
    }

    [Fact]
    public async Task LetzteEigenschaften_ErhaltenRohwertUndTatsaechlicheMeldezeit()
    {
        var handler = new DiagnosticHandler
        {
            Result = """{"properties":[{"code":"weight","value":7234,"time":1700000000123},{"code":"other","value":null}]}"""
        };
        var properties = await CreateClient(handler).GetReportedPropertiesAsync("device1", CancellationToken.None);
        Assert.Equal(7234, properties[0].Value.GetInt32());
        Assert.Equal(DateTimeOffset.FromUnixTimeMilliseconds(1700000000123), properties[0].ReportedAtUtc);
        Assert.Null(properties[1].ReportedAtUtc);
        Assert.Equal(JsonValueKind.Null, properties[1].Value.ValueKind);
        Assert.Equal("/v2.0/cloud/thing/device1/shadow/properties", handler.Paths.Last());
        Assert.All(handler.Methods, method => Assert.Equal(HttpMethod.Get, method));
    }

    [Fact]
    public async Task UngueltigeZeit_WirdNichtDurchJetztErsetzt()
    {
        var handler = new DiagnosticHandler { Result = """{"properties":[{"code":"weight","value":7234,"time":9223372036854775807}]}""" };
        var property = Assert.Single(await CreateClient(handler).GetReportedPropertiesAsync("device1", CancellationToken.None));
        Assert.Null(property.ReportedAtUtc);
    }

    [Fact]
    public async Task FehlendeBerechtigung_WirdNichtAlsLeererMesswertAusgegeben()
    {
        var handler = new DiagnosticHandler { Refused = true };
        var error = await Assert.ThrowsAsync<TuyaApiException>(() =>
            CreateClient(handler).GetReportedPropertiesAsync("device1", CancellationToken.None));
        Assert.Equal("1106", error.TuyaCode);
    }

    [Fact]
    public async Task UnerwarteteAntwort_WirdAbgelehnt()
    {
        var handler = new DiagnosticHandler { Result = "[]" };
        await Assert.ThrowsAsync<JsonException>(() =>
            CreateClient(handler).GetDeviceSpecificationAsync("device1", CancellationToken.None));
    }

    private class DiagnosticHandler : HttpMessageHandler
    {
        public string Result { get; init; } = "{}";
        public bool Refused { get; init; }
        public List<string> Paths { get; } = [];
        public List<HttpMethod> Methods { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Paths.Add(request.RequestUri!.PathAndQuery);
            Methods.Add(request.Method);
            var body = request.RequestUri.AbsolutePath == "/v1.0/token"
                ? """{"success":true,"result":{"access_token":"test-token","expire_time":3600}}"""
                : Refused ? """{"success":false,"code":"1106","msg":"permission deny"}"""
                : "{\"success\":true,\"result\":" + Result + "}";
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json")
            });
        }
    }
}
