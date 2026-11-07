using System.Text.Json;
using HomeControl.Api.Tests.Fakes;
using HomeControl.Api.Tuya;

namespace HomeControl.Api.Tests;

// Keine Hardware wird geschaltet: Der HttpClient spricht nur mit FakeTuyaHandler.
public class TuyaCommandTests
{
    private readonly FakeTuyaHandler handler = new();
    private readonly FakeTimeProvider time = new();
    private readonly TestOptionsMonitor<TuyaOptions> options = new(new TuyaOptions
    {
        ClientId = "test-client",
        ClientSecret = "test-secret",
    });

    private TuyaClient Client()
    {
        var http = new HttpClient(handler, disposeHandler: false)
        {
            BaseAddress = new Uri("https://openapi.tuyaeu.com"),
        };
        return new TuyaClient(http, options, new TuyaTokenStore(time), time);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Schaltbefehl_sendet_genau_einen_Switch1_Wert(bool on)
    {
        var accepted = await Client().SendSwitchCommandAsync("test-device", "switch_1", on, CancellationToken.None);

        Assert.True(accepted);
        var body = Assert.Single(handler.CommandBodies);
        using var json = JsonDocument.Parse(body);
        var command = Assert.Single(json.RootElement.GetProperty("commands").EnumerateArray());
        Assert.Equal("switch_1", command.GetProperty("code").GetString());
        Assert.Equal(on, command.GetProperty("value").GetBoolean());
        Assert.DoesNotContain("test-secret", body);
    }

    [Fact]
    public async Task Tuya_result_false_ist_keine_Bestaetigung()
    {
        handler.CommandResponse = _ => "{ \"success\": true, \"result\": false }";

        var accepted = await Client().SendSwitchCommandAsync("test-device", "switch_1", true, CancellationToken.None);

        Assert.False(accepted);
        Assert.Single(handler.CommandBodies);
    }

    [Fact]
    public async Task Fehler_von_Tuya_wird_nicht_als_Erfolg_bewertet()
    {
        handler.CommandResponse = _ => TuyaJson.Error("1106", "permission deny");

        var error = await Assert.ThrowsAsync<TuyaApiException>(
            () => Client().SendSwitchCommandAsync("test-device", "switch_1", true, CancellationToken.None));

        Assert.Equal("1106", error.TuyaCode);
        Assert.Single(handler.CommandBodies);
    }
}
