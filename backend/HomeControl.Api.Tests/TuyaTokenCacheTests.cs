using HomeControl.Api.Tests.Fakes;
using HomeControl.Api.Tuya;

namespace HomeControl.Api.Tests;

// Tests für den Token-Cache (TuyaTokenStore) im Zusammenspiel mit dem TuyaClient.
// Es geht keine Anfrage ins Internet: FakeTuyaHandler spielt die Tuya-Cloud.
public class TuyaTokenCacheTests
{
    private readonly FakeTuyaHandler handler = new();
    private readonly FakeTimeProvider time = new();
    private readonly TestOptionsMonitor<TuyaOptions> options = new(new TuyaOptions
    {
        ClientId = "test-client",
        ClientSecret = "test-secret",
        TestDeviceId = "test-device",
    });
    private readonly TuyaTokenStore store;

    public TuyaTokenCacheTests()
    {
        store = new TuyaTokenStore(time);
    }

    // Wie in der App: Jeder Abruf bekommt einen neuen TuyaClient (transient), der Speicher bleibt derselbe.
    private Task<List<TuyaStatusItem>> GetStatusAsync()
    {
        var http = new HttpClient(handler, disposeHandler: false) { BaseAddress = new Uri("https://openapi.tuyaeu.com") };
        var client = new TuyaClient(http, options, store, time);
        return client.GetDeviceStatusAsync("test-device", CancellationToken.None);
    }

    [Fact]
    public async Task ZweiterAbruf_NutztGespeichertesToken()
    {
        await GetStatusAsync();
        await GetStatusAsync();

        Assert.Equal(1, handler.TokenRequestCount);
        Assert.Equal(["token-1", "token-1"], handler.StatusAccessTokens);
    }

    [Fact]
    public async Task Bis61SekundenVorAblauf_BleibtTokenGueltig()
    {
        await GetStatusAsync();

        // Laufzeit 7200 s: 7139 s später sind es noch 61 s bis zum Ablauf
        time.Advance(TimeSpan.FromSeconds(7139));
        await GetStatusAsync();

        Assert.Equal(1, handler.TokenRequestCount);
    }

    [Fact]
    public async Task Ab60SekundenVorAblauf_WirdNeuesTokenGeholt()
    {
        await GetStatusAsync();

        // 7140 s später sind es genau 60 s bis zum Ablauf: jetzt neu holen
        time.Advance(TimeSpan.FromSeconds(7140));
        await GetStatusAsync();

        Assert.Equal(2, handler.TokenRequestCount);
        Assert.Equal(["token-1", "token-2"], handler.StatusAccessTokens);
    }

    [Fact]
    public async Task ParalleleAbrufe_HolenNurEinToken()
    {
        // Die Token-Antwort wird zurückgehalten, bis alle Abrufe gestartet sind.
        var gate = new TaskCompletionSource();
        handler.TokenGate = gate.Task;

        var calls = Enumerable.Range(0, 5).Select(_ => GetStatusAsync()).ToList();
        await handler.FirstTokenRequestStarted.Task;
        gate.SetResult();
        await Task.WhenAll(calls);

        Assert.Equal(1, handler.TokenRequestCount);
        Assert.Equal(5, handler.StatusAccessTokens.Count);
        Assert.All(handler.StatusAccessTokens, token => Assert.Equal("token-1", token));
    }

    [Fact]
    public async Task TokenFehlerImBody_HoltNeuesTokenUndWiederholtEinmal()
    {
        // Erster Statusabruf: Tuya meldet trotz HTTP 200 ein abgelaufenes Token
        handler.StatusResponse = (number, _) => number == 1
            ? TuyaJson.Error("1010", "token is expired")
            : TuyaJson.Status(true);

        var status = await GetStatusAsync();

        Assert.True(status.Single(item => item.Code == "switch_1").Value.GetBoolean());
        Assert.Equal(2, handler.TokenRequestCount);
        Assert.Equal(["token-1", "token-2"], handler.StatusAccessTokens);

        // Das neue Token wird danach weiterverwendet
        await GetStatusAsync();
        Assert.Equal(2, handler.TokenRequestCount);
        Assert.Equal("token-2", handler.StatusAccessTokens[^1]);
    }

    [Fact]
    public async Task TokenFehlerZweimal_WirdNichtEinDrittesMalVersucht()
    {
        handler.StatusResponse = (_, _) => TuyaJson.Error("1011", "token invalid");

        var error = await Assert.ThrowsAsync<TuyaApiException>(GetStatusAsync);

        Assert.Equal("1011", error.TuyaCode);
        Assert.Equal(2, handler.StatusAccessTokens.Count);
        Assert.Equal(2, handler.TokenRequestCount);
    }

    [Fact]
    public async Task AndererFehler_WirdNichtWiederholt()
    {
        handler.StatusResponse = (_, _) => TuyaJson.Error("1106", "permission deny");

        var error = await Assert.ThrowsAsync<TuyaApiException>(GetStatusAsync);

        Assert.Equal("1106", error.TuyaCode);
        Assert.Single(handler.StatusAccessTokens);
        Assert.Equal(1, handler.TokenRequestCount);
    }

    [Fact]
    public async Task FehlgeschlagenerTokenAbruf_WirdNichtGespeichert()
    {
        handler.TokenResponse = number => number == 1
            ? TuyaJson.Error("1004", "sign invalid")
            : TuyaJson.Token("token-2", 7200);

        await Assert.ThrowsAsync<TuyaApiException>(GetStatusAsync);
        await GetStatusAsync();

        Assert.Equal(2, handler.TokenRequestCount);
        Assert.Equal(["token-2"], handler.StatusAccessTokens);
    }

    [Fact]
    public async Task AndereClientId_HoltNeuesToken()
    {
        await GetStatusAsync();

        options.CurrentValue = new TuyaOptions
        {
            ClientId = "andere-client",
            ClientSecret = "test-secret",
            TestDeviceId = "test-device",
        };
        await GetStatusAsync();

        Assert.Equal(2, handler.TokenRequestCount);
    }

    [Fact]
    public async Task OhneExpireTime_WirdTokenNichtWiederverwendet()
    {
        handler.TokenResponse = number => TuyaJson.Token($"token-{number}", expireTime: null);

        await GetStatusAsync();
        await GetStatusAsync();

        Assert.Equal(2, handler.TokenRequestCount);
    }

    [Fact]
    public async Task AbgelehntesToken_WirdNichtErneutAusgegeben_NeueresSchon()
    {
        var fetchCount = 0;
        Task<TuyaToken> Fetch(CancellationToken _) =>
            Task.FromResult(new TuyaToken($"token-{++fetchCount}", TimeSpan.FromHours(2)));

        var first = await store.GetAccessTokenAsync("test-client", Fetch, CancellationToken.None);
        var second = await store.GetAccessTokenAsync("test-client", Fetch, CancellationToken.None, rejectedToken: first);
        // Ein zweiter Abruf, dem ebenfalls token-1 abgelehnt wurde, bekommt das schon geholte token-2.
        var third = await store.GetAccessTokenAsync("test-client", Fetch, CancellationToken.None, rejectedToken: first);

        Assert.Equal("token-1", first);
        Assert.Equal("token-2", second);
        Assert.Equal("token-2", third);
        Assert.Equal(2, fetchCount);
    }
}
