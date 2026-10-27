using Microsoft.Extensions.Options;

namespace HomeControl.Api.Tests.Fakes;

// Einfacher IOptionsMonitor: CurrentValue kann im Test ausgetauscht werden
// (z. B. um eine geänderte ClientId zu simulieren).
public class TestOptionsMonitor<T>(T value) : IOptionsMonitor<T>
{
    public T CurrentValue { get; set; } = value;

    public T Get(string? name) => CurrentValue;

    public IDisposable? OnChange(Action<T, string?> listener) => null;
}
