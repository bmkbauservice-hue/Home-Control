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
    public void UnbekannteCodes_WerdenNichtGespeichert()
    {
        // Temperatur liefert bisher kein verknüpftes Gerät, der Faktor ist unbekannt: nichts raten.
        var result = Apply(socket, Status(("va_temperature", "215")));

        Assert.Equal(TuyaSyncResults.UnknownValue, result);
        Assert.Null(socket.LastValue);
        Assert.Null(socket.MeasuredAtUtc);
        Assert.Empty(Events(socket));
    }

    // --- Messwerte: genau die Rohwerte, die tuya-rohwerte-anzeigen.sh am 02.11.2026 gezeigt hat ---

    [Fact]
    public void Barlicht_EchteRohwerte_SchalterUndMesswerte()
    {
        var result = Apply(socket, Status(
            ("switch_1", "false"),
            ("cur_current", "0"),
            ("cur_power", "0"),
            ("cur_voltage", "2271"),
            ("relay_status", "\"power_on\"")));

        Assert.Equal(TuyaSyncResults.Changed, result);
        Assert.Equal("AUS", socket.LastValue);
        Assert.Equal(0.0, socket.PowerWatt);
        Assert.Equal(227.1, socket.VoltageVolt!.Value, precision: 3);
        Assert.Null(socket.BatteryPercent);
        Assert.Equal(RetrievedAt, socket.MeasuredAtUtc);
        // Nur das Schalt-Ereignis, Messwerte erzeugen keins.
        Assert.Equal("turned_off", Assert.Single(Events(socket)).Action);
    }

    [Fact]
    public void Tuerkontakt_EchteRohwerte_ZuUndAkku()
    {
        var result = Apply(door, Status(("doorcontact_state", "false"), ("battery_percentage", "83")));

        Assert.Equal(TuyaSyncResults.Changed, result);
        Assert.Equal("ZU", door.LastValue);
        Assert.Equal(83, door.BatteryPercent);
        Assert.Null(door.PowerWatt);
        Assert.Equal("door_closed", Assert.Single(Events(door)).Action);
    }

    [Fact]
    public void Leistung_Aendert_sich_ohne_neues_Ereignis()
    {
        Apply(socket, Status(("switch_1", "true"), ("cur_power", "605")));

        var result = Apply(socket, Status(("switch_1", "true"), ("cur_power", "1234")));

        Assert.Equal(TuyaSyncResults.Unchanged, result);
        Assert.Equal(123.4, socket.PowerWatt!.Value, precision: 3);
        Assert.Single(Events(socket));
    }

    [Fact]
    public void NurMesswerte_ErgebnisMeasured()
    {
        var result = Apply(door, Status(("battery_percentage", "12")));

        Assert.Equal(TuyaSyncResults.Measured, result);
        Assert.Equal(12, door.BatteryPercent);
        Assert.Null(door.LastValue);
        Assert.Empty(Events(door));
    }

    [Fact]
    public void Offline_MesswerteBleibenUnveraendert()
    {
        Apply(socket, Status(("cur_voltage", "2300")));

        // Tuya liefert für ein Offline-Gerät den letzten bekannten Stand, der zählt nicht als neu.
        var later = RetrievedAt.AddMinutes(5);
        var result = TuyaDeviceSyncer.Apply(db, socket, Status(("switch_1", "true"), ("cur_voltage", "2271")), later, online: false);
        db.SaveChanges();

        Assert.Equal(TuyaSyncResults.Changed, result);
        Assert.Equal(230.0, socket.VoltageVolt!.Value, precision: 3);
        Assert.Equal(RetrievedAt, socket.MeasuredAtUtc);
    }

    [Theory]
    [InlineData("battery_percentage", "\"83\"")]
    [InlineData("battery_percentage", "150")]
    [InlineData("battery_percentage", "-1")]
    [InlineData("cur_power", "12.5")]
    [InlineData("cur_power", "true")]
    [InlineData("cur_voltage", "-5")]
    public void UnplausibleMesswerte_WerdenNichtGeraten(string code, string json)
    {
        var result = Apply(door, Status((code, json)));

        Assert.Equal(TuyaSyncResults.UnknownValue, result);
        Assert.Null(door.BatteryPercent);
        Assert.Null(door.PowerWatt);
        Assert.Null(door.VoltageVolt);
        Assert.Null(door.MeasuredAtUtc);
    }

    [Fact]
    public void LeereStatusliste_IstUnbekannt()
    {
        Assert.Equal(TuyaSyncResults.UnknownValue, Apply(socket, []));
    }
}
