using System.Text.Json;
using HomeControl.Api.Data;
using HomeControl.Api.Models;
using HomeControl.Api.Tuya;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace HomeControl.Api.Tests;

// Tests für TuyaDeviceSyncer.Apply: Wie ein Tuya-Status auf ein Gerät in der Datenbank übertragen wird.
// Eigene SQLite-Datenbank im Arbeitsspeicher, es geht keine Anfrage an Tuya.
public class TuyaDeviceSyncerTests : IDisposable
{
    private static readonly DateTimeOffset RetrievedAt = new(2026, 10, 29, 12, 0, 5, 321, TimeSpan.Zero);

    private readonly SqliteConnection connection;
    private readonly ApplicationDbContext db;
    private readonly Device socket;
    private readonly Device door;

    public TuyaDeviceSyncerTests()
    {
        connection = new SqliteConnection("DataSource=:memory:");
        connection.Open();

        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(connection)
            .Options;
        db = new ApplicationDbContext(options);
        db.Database.EnsureCreated();

        var room = new Room { Name = "Flur" };
        socket = new Device { Name = "Steckdose Flur", Type = "Socket", Room = room };
        door = new Device { Name = "Haustür", Type = "DoorSensor", Room = room };
        db.Devices.AddRange(socket, door);
        db.SaveChanges();
    }

    public void Dispose()
    {
        db.Dispose();
        connection.Dispose();
    }

    // Statusliste wie von Tuya, z. B. Status(("switch_1", "true")).
    private static List<TuyaStatusItem> Status(params (string Code, string Json)[] items) =>
        items.Select(i => new TuyaStatusItem(i.Code, JsonDocument.Parse(i.Json).RootElement.Clone())).ToList();

    private string Apply(Device device, List<TuyaStatusItem> status)
    {
        var result = TuyaDeviceSyncer.Apply(db, device, status, RetrievedAt);
        db.SaveChanges();
        return result;
    }

    private List<DeviceEvent> Events(Device device) =>
        db.DeviceEvents.AsNoTracking().Where(e => e.DeviceId == device.Id).OrderBy(e => e.Timestamp).ToList();

    [Fact]
    public void Steckdose_Ein_SetztZustandUndSpeichertEreignis()
    {
        var result = Apply(socket, Status(("switch_1", "true")));

        Assert.Equal(TuyaSyncResults.Changed, result);
        Assert.True(socket.IsActive);
        Assert.Equal("EIN", socket.LastValue);
        var saved = Assert.Single(Events(socket));
        Assert.Equal("turned_on", saved.Action);
        Assert.Equal(RetrievedAt, saved.Timestamp);
    }

    [Fact]
    public void Steckdose_GleicherZustand_SpeichertNichtNochmal()
    {
        Apply(socket, Status(("switch_1", "true")));

        var result = Apply(socket, Status(("switch_1", "true")));

        Assert.Equal(TuyaSyncResults.Unchanged, result);
        Assert.Single(Events(socket));
    }

    [Fact]
    public void Steckdose_Wechsel_AufAus_SpeichertZweitesEreignis()
    {
        Apply(socket, Status(("switch_1", "true")));

        var result = Apply(socket, Status(("switch_1", "false")));

        Assert.Equal(TuyaSyncResults.Changed, result);
        Assert.False(socket.IsActive);
        Assert.Equal("AUS", socket.LastValue);
        Assert.Equal(["turned_on", "turned_off"], Events(socket).Select(e => e.Action).ToArray());
    }

    [Fact]
    public void Tuerkontakt_Offen_SetztOffenUndSpeichertDoorOpened()
    {
        var result = Apply(door, Status(("doorcontact_state", "true")));

        Assert.Equal(TuyaSyncResults.Changed, result);
        Assert.Equal("OFFEN", door.LastValue);
        Assert.Equal("door_opened", Assert.Single(Events(door)).Action);
    }

    [Fact]
    public void Tuerkontakt_Zu_nachOffen_SpeichertDoorClosed()
    {
        Apply(door, Status(("doorcontact_state", "true")));

        var result = Apply(door, Status(("doorcontact_state", "false")));

        Assert.Equal(TuyaSyncResults.Changed, result);
        Assert.Equal("ZU", door.LastValue);
        Assert.Equal(["door_opened", "door_closed"], Events(door).Select(e => e.Action).ToArray());
    }

    [Fact]
    public void Tuerkontakt_GleicherZustand_SpeichertNichtNochmal()
    {
        Apply(door, Status(("doorcontact_state", "false")));

        var result = Apply(door, Status(("doorcontact_state", "false")));

        Assert.Equal(TuyaSyncResults.Unchanged, result);
        Assert.Single(Events(door));
    }

    [Fact]
    public void Tuerkontakt_LaesstIsActiveUnveraendert()
    {
        Apply(door, Status(("doorcontact_state", "true")));

        Assert.False(door.IsActive);
    }

    [Theory]
    [InlineData("switch_1", "\"true\"")]
    [InlineData("switch_1", "1")]
    [InlineData("switch_1", "null")]
    [InlineData("doorcontact_state", "\"open\"")]
    [InlineData("doorcontact_state", "0")]
    public void KeinEchterBoolean_WirdNichtGeraten(string code, string json)
    {
        var result = Apply(socket, Status((code, json)));

        Assert.Equal(TuyaSyncResults.UnknownValue, result);
        Assert.Null(socket.LastValue);
        Assert.Empty(Events(socket));
    }

    [Fact]
    public void UnbekannteCodes_WerdenNochNichtGespeichert()
    {
        // Temperatur, Leistung und Akku folgen, sobald die echten Rohwerte bekannt sind.
        var result = Apply(socket, Status(("va_temperature", "215"), ("cur_power", "1234"), ("battery_percentage", "80")));

        Assert.Equal(TuyaSyncResults.UnknownValue, result);
        Assert.Null(socket.LastValue);
        Assert.Empty(Events(socket));
    }

    [Fact]
    public void LeereStatusliste_IstUnbekannt()
    {
        Assert.Equal(TuyaSyncResults.UnknownValue, Apply(socket, []));
    }
}
