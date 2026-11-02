using HomeControl.Api.Data;
using HomeControl.Api.Models;
using HomeControl.Api.Status;
using HomeControl.Api.Tests.Fakes;
using HomeControl.Api.Tuya;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
// Microsofts Test-Uhr kann Timer. Eigener Name, weil es in Fakes schon eine FakeTimeProvider gibt.
using FakeClock = Microsoft.Extensions.Time.Testing.FakeTimeProvider;

namespace HomeControl.Api.Tests;

// Tests für den Hintergrund-Abgleich: echter TuyaDeviceSyncer, eigene SQLite-Datenbank im Arbeitsspeicher,
// Tuya-Cloud gespielt von FakeTuyaHandler. Die Zeit stellt der Test vor, es wird nie 15 Minuten gewartet.
public class TuyaSyncBackgroundServiceTests : IDisposable
{
    private const string DoorTuyaId = "tuya-tuer";

    private readonly SqliteConnection connection = new("DataSource=:memory:");
    private readonly FakeClock clock = new();
    private readonly FakeTuyaHandler tuya = new();
    private readonly CapturingLoggerProvider logs = new();
    private readonly TestOptionsMonitor<TuyaOptions> options = new(new TuyaOptions
    {
        BaseUrl = "https://tuya.test",
        ClientId = "client-id",
        ClientSecret = "client-secret",
        AutoSyncSeconds = 60,
    });
    private readonly ServiceProvider services;
    private readonly Guid doorId;

    public TuyaSyncBackgroundServiceTests()
    {
        connection.Open();

        // Fast wie in Program.cs, nur mit Test-Uhr, Fake-Cloud und Test-Datenbank.
        var collection = new ServiceCollection();
        collection.AddLogging(logging => logging.AddProvider(logs).SetMinimumLevel(LogLevel.Debug));
        collection.AddDbContext<ApplicationDbContext>(o => o.UseSqlite(connection));
        collection.AddSingleton<TimeProvider>(clock);
        collection.AddSingleton<IOptionsMonitor<TuyaOptions>>(options);
        collection.AddSingleton<TuyaTokenStore>();
        collection.AddSingleton<TuyaOnlineCache>();
        collection.AddScoped(sp => new TuyaClient(
            new HttpClient(tuya) { BaseAddress = new Uri("https://tuya.test") },
            options, sp.GetRequiredService<TuyaTokenStore>(), clock));
        collection.AddScoped<TuyaDeviceSyncer>();
        services = collection.BuildServiceProvider();

        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        db.Database.EnsureCreated();
        var door = new Device { Name = "Haustür", Type = "DoorSensor", Room = new Room { Name = "Flur" }, TuyaDeviceId = DoorTuyaId, LastValue = "ZU" };
        db.Devices.Add(door);
        db.SaveChanges();
        doorId = door.Id;

        // Die Tür ist jetzt offen (Tuya meldet doorcontact_state = true).
        tuya.StatusResponse = (_, _) => DoorStatus(open: true);
    }

    public void Dispose()
    {
        services.Dispose();
        connection.Dispose();
    }

    private static string DoorStatus(bool open) =>
        $$"""{ "success": true, "result": [ { "code": "doorcontact_state", "value": {{(open ? "true" : "false")}} } ] }""";

    private TuyaSyncBackgroundService CreateService(IServiceProvider? provider = null) => new(
        (provider ?? services).GetRequiredService<IServiceScopeFactory>(),
        options,
        services.GetRequiredService<TuyaOnlineCache>(),
        clock,
        services.GetRequiredService<ILogger<TuyaSyncBackgroundService>>());

    private Device LoadDoor()
    {
        using var scope = services.CreateScope();
        return scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().Devices.AsNoTracking().Single(d => d.Id == doorId);
    }

    private List<string> EventActions()
    {
        using var scope = services.CreateScope();
        return scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().DeviceEvents.AsNoTracking()
            .Where(e => e.DeviceId == doorId).Select(e => e.Action).ToList();
    }

    // Wartet, bis eine Bedingung erfüllt ist, die ein anderer Thread herbeiführt (der Hintergrund-Dienst).
    // Das echte Warten ist nur eine Sicherung gegen hängende Tests, die Zeit im Dienst steuert die Test-Uhr.
    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (!condition())
        {
            Assert.True(DateTime.UtcNow < deadline, "Bedingung wurde nicht rechtzeitig erfüllt.");
            await Task.Delay(10);
        }
    }

    // ---------- Ein Durchlauf ----------

    [Fact]
    public async Task Durchlauf_ruft_den_Abgleich_auf_und_speichert_das_Ereignis()
    {
        var ok = await CreateService().RunOnceAsync(CancellationToken.None);

        Assert.True(ok);
        Assert.Equal("OFFEN", LoadDoor().LastValue);
        Assert.Equal(["door_opened"], EventActions());
        // Die Online-Liste liegt jetzt im Speicher, aus dem /api/status liest.
        Assert.NotNull(services.GetRequiredService<TuyaOnlineCache>().Latest);
    }

    [Fact]
    public async Task Ohne_Zugangsdaten_wird_Tuya_nicht_gefragt()
    {
        options.CurrentValue = new TuyaOptions { BaseUrl = "https://tuya.test", AutoSyncSeconds = 60 };

        var ok = await CreateService().RunOnceAsync(CancellationToken.None);

        // Kein Fehler (kein Backoff), aber auch kein Aufruf.
        Assert.True(ok);
        Assert.Empty(tuya.StatusAccessTokens);
        Assert.Equal("ZU", LoadDoor().LastValue);
    }

    [Fact]
    public async Task Tuya_nicht_erreichbar_wird_geloggt_statt_geworfen()
    {
        tuya.StatusResponse = (_, _) => throw new HttpRequestException("Cloud weg");

        var ok = await CreateService().RunOnceAsync(CancellationToken.None);

        Assert.False(ok);
        Assert.Contains(logs.Entries, e => e.Level == LogLevel.Warning && e.Message.Contains("Kein Gerät konnte gelesen werden"));
        // Die Zugangsdaten tauchen in keiner Log-Zeile auf.
        Assert.DoesNotContain(logs.Entries, e => e.Message.Contains("client-secret"));
    }

    [Fact]
    public async Task Unerwarteter_Fehler_wird_geloggt_statt_geworfen()
    {
        // Ein ServiceProvider ohne TuyaDeviceSyncer: GetRequiredService wirft eine InvalidOperationException.
        using var broken = new ServiceCollection().BuildServiceProvider();

        var ok = await CreateService(broken).RunOnceAsync(CancellationToken.None);

        Assert.False(ok);
        Assert.Contains(logs.Entries, e => e.Level == LogLevel.Error && e.Message.Contains("InvalidOperationException"));
    }

    // ---------- Takt, Backoff, aus ----------

    [Theory]
    [InlineData(0, 60)]
    [InlineData(1, 120)]
    [InlineData(2, 240)]
    [InlineData(5, 1920)]
    [InlineData(6, 3600)]   // 3840 s wäre länger als eine Stunde → Obergrenze
    [InlineData(50, 3600)]
    public void Backoff_verdoppelt_bis_hoechstens_eine_Stunde(int failures, int expectedSeconds)
    {
        var delay = TuyaSyncBackgroundService.GetDelay(TimeSpan.FromSeconds(60), failures);

        Assert.Equal(TimeSpan.FromSeconds(expectedSeconds), delay);
    }

    [Theory]
    [InlineData(0, null)]
    [InlineData(-5, null)]
    [InlineData(10, 60)]     // unter der Untergrenze → 60
    [InlineData(900, 900)]
    public void AutoSyncSeconds_wird_richtig_gelesen(int seconds, int? expectedSeconds)
    {
        var interval = new TuyaOptions { AutoSyncSeconds = seconds }.GetAutoSyncInterval();

        Assert.Equal(expectedSeconds is null ? null : TimeSpan.FromSeconds(expectedSeconds.Value), interval);
    }

    [Fact]
    public async Task Null_heisst_aus()
    {
        options.CurrentValue.AutoSyncSeconds = 0;
        var service = CreateService();

        await service.StartAsync(CancellationToken.None);

        // ExecuteAsync endet von selbst (ohne Stoppen), es wurde nichts abgefragt.
        // Seit .NET 10 läuft ExecuteAsync im Hintergrund an, deshalb auf das Ende warten (Sicherung: 10 s).
        await service.ExecuteTask!.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Null(service.CurrentPeriod);
        Assert.Empty(tuya.StatusAccessTokens);
        await service.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task Laeuft_im_Takt_wird_bei_Fehlern_langsamer_und_stoppt_sauber()
    {
        var service = CreateService();
        var cache = services.GetRequiredService<TuyaOnlineCache>();
        await service.StartAsync(CancellationToken.None);

        // 1. Durchlauf gleich beim Start. Solange der Dienst läuft, liest /api/status nur den Speicher.
        await WaitUntilAsync(() => tuya.StatusAccessTokens.Count == 1);
        Assert.True(cache.BackgroundRefreshActive);

        // 2. Durchlauf nach einer Minute (Test-Uhr vorstellen, nicht warten).
        clock.Advance(TimeSpan.FromSeconds(60));
        await WaitUntilAsync(() => tuya.StatusAccessTokens.Count == 2);

        // Jetzt ist die Cloud weg: 3. Durchlauf schlägt fehl, der Abstand verdoppelt sich auf 2 Minuten.
        tuya.StatusResponse = (_, _) => throw new HttpRequestException("Cloud weg");
        clock.Advance(TimeSpan.FromSeconds(60));
        await WaitUntilAsync(() => service.CurrentPeriod == TimeSpan.FromSeconds(120));
        Assert.Equal(3, tuya.StatusAccessTokens.Count);

        // Nach einer Minute passiert nichts, erst nach zwei Minuten der 4. Durchlauf.
        clock.Advance(TimeSpan.FromSeconds(60));
        clock.Advance(TimeSpan.FromSeconds(60));
        await WaitUntilAsync(() => tuya.StatusAccessTokens.Count == 4);

        // Beenden der App: kein Fehler, ExecuteAsync endet sauber.
        await service.StopAsync(CancellationToken.None);
        Assert.True(service.ExecuteTask!.IsCompletedSuccessfully);
        Assert.Equal(4, tuya.StatusAccessTokens.Count);
        // Nach dem Stoppen fragt /api/status wieder selbst (Rückfallebene).
        Assert.False(cache.BackgroundRefreshActive);
    }
}
