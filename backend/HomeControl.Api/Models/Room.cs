namespace HomeControl.Api.Models;

public class Room
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string Name { get; set; } = string.Empty;

    public List<Device> Devices { get; set; } = [];
}
