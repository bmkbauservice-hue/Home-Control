namespace HomeControl.Api.Models;

public class Device
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string Name { get; set; } = string.Empty;

    public string Type { get; set; } = string.Empty;

    public bool IsActive { get; set; }

    public string? LastValue { get; set; }

    // Id des Geräts in der Tuya-Cloud, wenn es mit einem echten Gerät verknüpft ist.
    // Kein Geheimnis (ohne Client-Secret nutzlos), aber nur in der lokalen Datenbank, nicht in Git.
    // Jedes Tuya-Gerät kann höchstens mit einem Gerät verknüpft sein (eindeutiger Index).
    public string? TuyaDeviceId { get; set; }

    public Guid RoomId { get; set; }

    public Room Room { get; set; } = null!;

    public List<DeviceEvent> Events { get; set; } = [];
}
