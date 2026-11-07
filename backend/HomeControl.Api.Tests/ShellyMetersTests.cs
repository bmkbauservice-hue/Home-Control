using System.Net;
using HomeControl.Api.LocalDevices;
using Microsoft.Extensions.Configuration;

namespace HomeControl.Api.Tests;

public class ShellyMetersTests
{
    [Fact]
    public async Task LiestWattUndRechnetDenZaehlerVonWhInKwhUm()
    {
        using var handler = new MeterHandler("""{"id":0,"apower":0,"aenergy":{"total":1280}}""");
        var service = new ShellyMeters(Config("192.168.20.42"), new ClientFactory(handler));

        var meter = Assert.Single(await service.ReadAsync(default));
        Assert.Equal("online", meter.State);
        Assert.Equal(0d, meter.Watt);
        Assert.Equal(1.28d, meter.CounterKwh);
        Assert.Equal("/rpc/Switch.GetStatus?id=0", handler.Path);
        await service.ReadAsync(default);
        Assert.Equal(1, handler.Requests);
    }

    [Theory]
    [InlineData("127.0.0.1")]
    [InlineData("8.8.8.8")]
    [InlineData("localhost")]
    public async Task UngeeigneteAdresseWirdNichtAngefragt(string address)
    {
        using var handler = new MeterHandler("{}");
        var service = new ShellyMeters(Config(address), new ClientFactory(handler));
        Assert.Equal("configuration", Assert.Single(await service.ReadAsync(default)).State);
        Assert.Equal(0, handler.Requests);
    }

    [Fact]
    public async Task OhneMessungBleibtDerWertLeer()
    {
        using var handler = new MeterHandler("""{"id":0,"output":true}""");
        var service = new ShellyMeters(Config("192.168.20.42"), new ClientFactory(handler));
        var meter = Assert.Single(await service.ReadAsync(default));
        Assert.Null(meter.Watt);
        Assert.Null(meter.CounterKwh);
    }

    private static IConfiguration Config(string ip) => new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?> { ["LocalDevices:Shelly:0:Ip"] = ip })
        .Build();

    private sealed class ClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }

    private sealed class MeterHandler(string json) : HttpMessageHandler
    {
        public int Requests { get; private set; }
        public string? Path { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Requests++;
            Path = request.RequestUri?.PathAndQuery;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json) });
        }
    }
}
