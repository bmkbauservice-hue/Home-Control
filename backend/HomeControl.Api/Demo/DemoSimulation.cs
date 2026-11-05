namespace HomeControl.Api.Demo;

// Simulierte Tür im Demo-Modus: Überschreibt den Zeitplan aus DemoBehavior, bis zum nächsten Umschalten oder Neustart.
// Damit lässt sich die Live-Anzeige auf Knopfdruck vorführen und testen, ohne auf den Zeitplan zu warten.
// Singleton, die Fake-Cloud (DemoTuyaHandler) liest den Wert bei jeder Statusabfrage.
public class DemoSimulation
{
    private readonly Lock sync = new();
    private bool? doorOpen;

    public bool? DoorOpen
    {
        get { lock (sync) return doorOpen; }
    }

    public void SetDoor(bool open)
    {
        lock (sync) doorOpen = open;
    }
}
