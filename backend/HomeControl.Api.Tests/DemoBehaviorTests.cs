using HomeControl.Api.Demo;
using HomeControl.Api.Tests.Fakes;

namespace HomeControl.Api.Tests;

// Verhalten der Fake-Geräte und Kameras im Demo-Modus: plausibel, schwankend, wiederholbar.
public class DemoBehaviorTests
{
    private static readonly DateTimeOffset Morning = new(2026, 11, 9, 9, 0, 0, TimeSpan.FromHours(1));   // Mo 09:00 Berlin

    private static DemoDevice Device(string key) => DemoHouse.Devices.Single(d => d.Key == key);

    [Fact]
    public void Beispielhaus_hat_5_Raeume_und_12_Geraete_mit_eindeutigen_Ids()
    {
        Assert.Equal(5, DemoHouse.Rooms.Length);
        Assert.Equal(12, DemoHouse.Devices.Length);
        Assert.Equal(12, DemoHouse.Devices.Select(d => d.TuyaId).Distinct().Count());
        Assert.All(DemoHouse.Devices, d => Assert.Contains(d.Room, DemoHouse.Rooms));
    }

    [Fact]
    public void Tuer_geht_ab_und_zu_auf_und_der_3_Minuten_Takt_erwischt_jedes_Oeffnen()
    {
        var door = Device("haustuer");
        var minutes = Enumerable.Range(0, 60).Select(m => DemoBehavior.StateAt(door, Morning.AddMinutes(m)).DoorOpen!.Value).ToList();

        // In einer Stunde dreimal offen, dazwischen zu.
        Assert.Equal(15, minutes.Count(open => open));
        // Abfrage alle 3 Minuten, egal mit welchem Versatz: Jedes Öffnen ist mindestens einmal dabei.
        for (var offset = 0; offset < 3; offset++)
        {
            var sampled = Enumerable.Range(0, 20).Select(i => minutes[(offset + 3 * i) % 60]).ToList();
            Assert.True(sampled.Count(open => open) >= 3);
        }
    }

    [Fact]
    public void Watt_schwankt_von_Minute_zu_Minute_bleibt_aber_plausibel()
    {
        var computer = Device("computer");
        var watts = Enumerable.Range(0, 30)
            .Select(m => DemoBehavior.StateAt(computer, Morning.AddMinutes(m)).PowerWatt!.Value)
            .ToList();

        Assert.True(watts.Distinct().Count() > 10);
        Assert.All(watts, w => Assert.InRange(w, 70, 150));
    }

    [Fact]
    public void Gleiche_Minute_gibt_gleiche_Werte()
    {
        var fridge = Device("kuehlschrank");

        Assert.Equal(DemoBehavior.StateAt(fridge, Morning), DemoBehavior.StateAt(fridge, Morning.AddSeconds(30)));
    }

    [Fact]
    public void Drucker_ist_offline_und_die_Tuer_hat_15_Prozent_Akku()
    {
        Assert.False(Device("drucker").Online);
        Assert.Single(DemoHouse.Devices, d => !d.Online);
        Assert.Equal(15, DemoBehavior.StateAt(Device("haustuer"), Morning).Battery);
    }

    [Fact]
    public void Historie_hat_7_Tage_nur_bekannte_Geraete_und_nichts_in_der_Zukunft()
    {
        var history = DemoDataSeeder.CreateHistory(Morning);

        Assert.All(history, e => Assert.NotNull(DemoHouse.Devices.SingleOrDefault(d => d.Key == e.Key)));
        Assert.All(history, e => Assert.True(e.Timestamp < Morning));
        var days = history.Select(e => e.Timestamp.ToOffset(TimeSpan.FromHours(1)).Date).Distinct().Count();
        Assert.Equal(7, days);
        Assert.Contains(history, e => e.Action == "temperature_changed" && e.Value!.EndsWith(" °C"));
    }

    [Fact]
    public async Task Neues_Bild_im_Demo_wechselt_das_Bild_und_macht_die_Aufnahmezeit_aktuell()
    {
        var clock = new FakeTimeProvider(Morning);
        var cameras = new DemoBlinkCameraClient(clock) { SnapshotDelay = TimeSpan.Zero };
        var before = await cameras.GetThumbnailAsync("9002", CancellationToken.None);
        Assert.True(Morning - before!.TakenAtUtc > TimeSpan.FromDays(1));   // alt: gelb in der Oberfläche

        clock.Advance(TimeSpan.FromMinutes(5));
        var result = await cameras.RequestNewThumbnailAsync("9002", CancellationToken.None);
        var after = await cameras.GetThumbnailAsync("9002", CancellationToken.None);

        Assert.True(result!.Completed);
        Assert.Equal(clock.GetUtcNow(), after!.TakenAtUtc);
        Assert.NotEqual(before.Content, after.Content);
        Assert.Null(await cameras.RequestNewThumbnailAsync("1234", CancellationToken.None));
    }
}
