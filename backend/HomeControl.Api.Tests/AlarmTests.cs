using System.Net;
using System.Net.Http.Json;
using HomeControl.Api.Alarm;
using HomeControl.Api.Blink;
using HomeControl.Api.Data;
using HomeControl.Api.Models;
using HomeControl.Api.Tests.Fakes;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace HomeControl.Api.Tests;

// Alarmseite: Blink ist gespielt, es wird kein echter Clip geladen.
public class AlarmTests
{
    private sealed class Factory : HomeControlApiFactory
    {
        public FakeBlinkCameraClient Blink { get; } = new();

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.ConfigureTestServices(services => services.AddSingleton<IBlinkCameraClient>(Blink));
        }
    }

    private static readonly byte[] Mp4 = [0, 0, 0, 24, .. "ftypisom"u8.ToArray(), 0, 0, 0, 1];

    private static async Task<HttpClient> AdminAsync(Factory factory) =>
        factory.CreateClientWithToken((await factory.GetTokensAsync()).AdminToken);

    private static async Task SeedRunAsync(Factory factory)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var room = new Room { Name = "Flur" };
        var door = new Device { Name = "Tür Sensor", Type = "DoorSensor", RoomId = room.Id };
        var scenario = new Scenario { Name = "Alarm", TriggerDeviceId = door.Id, BlinkSystemId = "1", BlinkSystemName = "Muju Home" };
        db.Rooms.Add(room);
        db.Devices.Add(door);
        db.Scenarios.Add(scenario);
        db.ScenarioRuns.Add(new ScenarioRun
        {
            ScenarioId = scenario.Id,
            StartedAtUtc = DateTimeOffset.UtcNow,
            FinishedAtUtc = DateTimeOffset.UtcNow,
            Trigger = "tuer",
            Outcome = ScenarioOutcomes.Confirmed,
            Message = "Blink hat das Scharfschalten bestätigt.",
        });
        await db.SaveChangesAsync();
    }

    [Theory]
    [InlineData("/api/alarm/events")]
    [InlineData("/api/alarm/clips")]
    [InlineData("/api/alarm/clips/l1/video")]
    public async Task Nur_Admins_sehen_die_Alarmdaten(string path)
    {
        using var factory = new Factory();
        using var anonymous = factory.CreateClient();
        using var user = factory.CreateClientWithToken((await factory.GetTokensAsync()).UserToken);

        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync(path)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await user.GetAsync(path)).StatusCode);
    }

    [Fact]
    public async Task Ereignis_erscheint_auch_ohne_Clip()
    {
        using var factory = new Factory();
        await SeedRunAsync(factory);
        using var admin = await AdminAsync(factory);

        var events = await admin.GetFromJsonAsync<List<AlarmEndpoints.AlarmEvent>>("/api/alarm/events");
        var clips = await admin.GetFromJsonAsync<AlarmEndpoints.AlarmClipsResponse>("/api/alarm/clips");

        var alarm = Assert.Single(events!);
        Assert.Equal("Tür Sensor", alarm.DoorName);
        Assert.Equal(ScenarioOutcomes.Confirmed, alarm.Outcome);
        Assert.Equal("Muju Home", alarm.BlinkSystemName);
        Assert.Empty(clips!.Clips);
    }

    [Fact]
    public async Task Clip_wird_erst_beim_Abruf_geladen_und_nicht_zwischengespeichert()
    {
        using var factory = new Factory();
        factory.Blink.Clips = new BlinkClipList([new BlinkClip("l7", "Flur", DateTimeOffset.UnixEpoch)], null);
        var loaded = 0;
        factory.Blink.ClipVideo = key =>
        {
            loaded++;
            return key == "l7" ? new BlinkClipVideo(Mp4) : null;
        };
        using var admin = await AdminAsync(factory);

        var clips = await admin.GetFromJsonAsync<AlarmEndpoints.AlarmClipsResponse>("/api/alarm/clips");
        Assert.Equal("l7", Assert.Single(clips!.Clips).Key);
        Assert.Equal(0, loaded);

        var video = await admin.GetAsync("/api/alarm/clips/l7/video");

        Assert.Equal(HttpStatusCode.OK, video.StatusCode);
        Assert.Equal("video/mp4", video.Content.Headers.ContentType!.MediaType);
        Assert.True(video.Headers.CacheControl!.NoStore);
        Assert.Equal(Mp4, await video.Content.ReadAsByteArrayAsync());
        Assert.Equal(1, loaded);
    }

    [Fact]
    public async Task Nicht_aufbereitbarer_Clip_wird_unveraendert_geliefert()
    {
        var result = await ClipRepair.RemoveEmptyPacketsAsync(Mp4, NullLogger.Instance, CancellationToken.None);
        Assert.Equal(Mp4, result);
    }

    [Fact]
    public async Task Unbekannter_Clip_ergibt_404()
    {
        using var factory = new Factory();
        using var admin = await AdminAsync(factory);
        Assert.Equal(HttpStatusCode.NotFound, (await admin.GetAsync("/api/alarm/clips/l999/video")).StatusCode);
    }

    [Fact]
    public async Task Ungueltiger_Schluessel_erreicht_Blink_nicht()
    {
        using var factory = new Factory();
        var called = false;
        factory.Blink.ClipVideo = _ => { called = true; return null; };
        using var admin = await AdminAsync(factory);

        var response = await admin.GetAsync("/api/alarm/clips/l1%2F..%2Fx/video");

        Assert.NotEqual(HttpStatusCode.OK, response.StatusCode);
        Assert.False(called);
    }
}
