namespace HomeControl.Api.Models;

public class DeviceEvent
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.UtcNow;

    public string Action { get; set; } = string.Empty;

    public string? Value { get; set; }

    public Guid DeviceId { get; set; }

    public Device Device { get; set; } = null!;
}
