using System.Text.Json;
using HomeControl.Api.Data;
using HomeControl.Api.Models;
using HomeControl.Api.Tuya;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace HomeControl.Api.Tests;

// Tests für das Übertragen des Tuya-Zustands (switch_1) auf ein Gerät in der Datenbank.
// Jeder Test bekommt eine eigene SQLite-Datenbank im Arbeitsspeicher.
// Die echte homecontrol.db wird nicht angefasst, und es geht keine Anfrage an Tuya.
public class TuyaSwitchRecorderTests : IDisposable
{
    private static readonly DateTimeOffset RetrievedAt = new(2026, 10, 27, 9, 30, 15, 123, TimeSpan.Zero);

    private readonly SqliteConnection connection;
    private readonly ApplicationDbContext db;
    private readonly TuyaSwitchRecorder recorder;
    private readonly Device lamp;

    public TuyaSwitchRecorderTests()
    {
        // Die Datenbank im Arbeitsspeicher lebt, solange die Verbindung offen ist.
        connection = new SqliteConnection("DataSource=:memory:");
        connection.Open();

        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(connection)
            .Options;
        db = new ApplicationDbContext(options);
        db.Database.EnsureCreated();

        var room = new Room { Name = "Wohnzimmer" };
        lamp = new Device { Name = "Licht Wohnzimmer", Type = "Light", Room = room };
        // Das Gerät hinzufügen nimmt den Raum über die Navigation automatisch mit.
        db.Devices.Add(lamp);
        db.SaveChanges();

        recorder = new TuyaSwitchRecorder(db);
    }

    public void Dispose()
    {
        db.Dispose();
        connection.Dispose();
    }

    // Baut eine Statusliste wie von Tuya, z. B. Status("true") für switch_1 = true.
    private static List<TuyaStatusItem> Status(string switchJson) =>
        [new TuyaStatusItem(TuyaSwitchRecorder.SwitchCode, JsonDocument.Parse(switchJson).RootElement.Clone())];

    private Task<string> RecordAsync(List<TuyaStatusItem> status, string? linkedDeviceId = null) =>
        recorder.RecordAsync(linkedDeviceId ?? lamp.Id.ToString(), status, RetrievedAt, CancellationToken.None);

    private List<DeviceEvent> SavedEvents() =>
        db.DeviceEvents.AsNoTracking().OrderBy(e => e.Timestamp).ToList();

    [Fact]
    public async Task ErsterAbruf_Ein_SetztGeraetUndSpeichertEreignis()
    {
        var result = await RecordAsync(Status("true"));

        Assert.Equal(TuyaSyncResults.Changed, result);
        Assert.True(lamp.IsActive);
        Assert.Equal("EIN", lamp.LastValue);

        var saved = Assert.Single(SavedEvents());
        Assert.Equal("turned_on", saved.Action);
        Assert.Null(saved.Value);
        Assert.Equal(RetrievedAt, saved.Timestamp);
    }

    [Fact]
    public async Task ErsterAbruf_Aus_SpeichertAuchEreignis()
    {
        // Vorher war der Zustand unbekannt (LastValue null), also ist AUS eine neue Information.
        var result = await RecordAsync(Status("false"));

        Assert.Equal(TuyaSyncResults.Changed, result);
        Assert.False(lamp.IsActive);
        Assert.Equal("AUS", lamp.LastValue);
        Assert.Equal("turned_off", Assert.Single(SavedEvents()).Action);
    }

    [Fact]
    public async Task GleicherZustand_SpeichertKeinWeiteresEreignis()
    {
        await RecordAsync(Status("true"));
        var result = await RecordAsync(Status("true"));

        Assert.Equal(TuyaSyncResults.Unchanged, result);
        Assert.Single(SavedEvents());
    }

    [Fact]
    public async Task Wechsel_EinZuAus_SpeichertZweitesEreignis()
    {
        await RecordAsync(Status("true"));
        var result = await RecordAsync(Status("false"));

        Assert.Equal(TuyaSyncResults.Changed, result);
        Assert.Equal("AUS", lamp.LastValue);
        Assert.Equal(new[] { "turned_on", "turned_off" }, SavedEvents().Select(e => e.Action));
    }

    [Theory]
    [InlineData("\"true\"")]
    [InlineData("1")]
    [InlineData("null")]
    public async Task KeinEchterBoolean_SpeichertNichts(string switchJson)
    {
        var result = await RecordAsync(Status(switchJson));

        Assert.Equal(TuyaSyncResults.UnknownValue, result);
        Assert.Null(lamp.LastValue);
        Assert.Empty(SavedEvents());
    }

    [Fact]
    public async Task Switch1Fehlt_SpeichertNichts()
    {
        var other = new List<TuyaStatusItem> { new("countdown_1", JsonDocument.Parse("0").RootElement.Clone()) };

        var result = await RecordAsync(other);

        Assert.Equal(TuyaSyncResults.UnknownValue, result);
        Assert.Empty(SavedEvents());
    }

    [Fact]
    public async Task NichtVerknuepft_SpeichertNichts()
    {
        var result = await recorder.RecordAsync("", Status("true"), RetrievedAt, CancellationToken.None);

        Assert.Equal(TuyaSyncResults.NotLinked, result);
        Assert.Empty(SavedEvents());
    }

    [Theory]
    [InlineData("kein-guid")]
    [InlineData("00000000-0000-0000-0000-000000000001")]
    public async Task UnbekanntesOderUngueltigesGeraet_SpeichertNichts(string linkedDeviceId)
    {
        var result = await RecordAsync(Status("true"), linkedDeviceId);

        Assert.Equal(TuyaSyncResults.DeviceNotFound, result);
        Assert.Null(lamp.LastValue);
        Assert.Empty(SavedEvents());
    }
}
