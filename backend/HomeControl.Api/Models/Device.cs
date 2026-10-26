namespace HomeControl.Api.Models;

public class Device
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string Name { get; set; } = string.Empty;

    public string Type { get; set; } = string.Empty;

    public bool IsActive { get; set; }

    public string? LastValue { get; set; }

    public Guid RoomId { get; set; }

    public Room Room { get; set; } = null!;

    public List<DeviceEvent> Events { get; set; } = [];
}
