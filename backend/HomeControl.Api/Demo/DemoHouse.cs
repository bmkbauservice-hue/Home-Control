namespace HomeControl.Api.Demo;

// Welche Art von Tuya-Gerät ein Demo-Gerät nachahmt. Bestimmt, welche Statuscodes der Fake liefert.
public enum DemoKind
{
    // switch_1
    Light,

    // switch_1, cur_power, cur_voltage (Steckdose mit Strommesser)
    Socket,

    // doorcontact_state, battery_percentage
    Door,

    // nur battery_percentage; die Temperatur kommt aus den Beispiel-Ereignissen (siehe DemoDataSeeder)
    Temperature,
}

// Ein Gerät im Beispielhaus. Key ist kurz und lesbar, daraus entsteht die erfundene Tuya-Id "demo<Key>" ohne
// Bindestriche: Echte Tuya-Ids bestehen nur aus Buchstaben und Ziffern, und DeviceEndpoints prüft das beim Speichern.
public record DemoDevice(
    string Key,
    string Name,
    string Type,
    string Room,
    DemoKind Kind,
    bool Online = true,
    int? Battery = null,
    int? SyncSeconds = null)
{
    public string TuyaId => "demo" + Key.Replace("-", "");
}

// Das Beispielhaus: 5 Räume, 12 Geräte. Seeder (Datenbank) und DemoTuyaHandler (Fake-Cloud) lesen beide
// von hier, damit Namen und Ids zusammenpassen. Alles erfunden, nichts stammt aus dem echten Haus.
public static class DemoHouse
{
    public const string Email = "demo@example.de";

    public static readonly string[] Rooms = ["Wohnzimmer", "Küche", "Schlafzimmer", "Flur", "Arbeitszimmer"];

    public static readonly DemoDevice[] Devices =
    [
        new("leselampe", "Leselampe", "Light", "Wohnzimmer", DemoKind.Light),
        new("fernseher", "Fernseher", "Socket", "Wohnzimmer", DemoKind.Socket),
        new("temp-wohnen", "Temperatur Wohnzimmer", "TemperatureSensor", "Wohnzimmer", DemoKind.Temperature, Battery: 76),
        new("deckenlicht", "Deckenlicht Küche", "Light", "Küche", DemoKind.Light),
        new("kaffee", "Kaffeemaschine", "Socket", "Küche", DemoKind.Socket),
        new("kuehlschrank", "Kühlschrank", "Socket", "Küche", DemoKind.Socket),
        new("nachttisch", "Nachttischlampe", "Light", "Schlafzimmer", DemoKind.Light),
        new("temp-schlafen", "Temperatur Schlafzimmer", "TemperatureSensor", "Schlafzimmer", DemoKind.Temperature, Battery: 64),
        // Akku 15 %: Die Übersicht zeigt "Batterien" gelb. Takt 3 Minuten wie bei der echten Tür.
        new("haustuer", "Haustür", "DoorSensor", "Flur", DemoKind.Door, Battery: 15, SyncSeconds: 180),
        new("schreibtisch", "Schreibtischlampe", "Light", "Arbeitszimmer", DemoKind.Light),
        new("computer", "Computer", "Socket", "Arbeitszimmer", DemoKind.Socket),
        // Ausgesteckt: Tuya meldet es offline, die LED ist rot.
        new("drucker", "Drucker", "Socket", "Arbeitszimmer", DemoKind.Socket, Online: false),
    ];

    public static DemoDevice? FindByTuyaId(string tuyaId) => Devices.FirstOrDefault(d => d.TuyaId == tuyaId);
}
