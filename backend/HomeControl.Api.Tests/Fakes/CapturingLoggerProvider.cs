using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace HomeControl.Api.Tests.Fakes;

// Sammelt alle Log-Meldungen der API im Arbeitsspeicher, damit ein Test prüfen kann, was im Log
// steht (und was NICHT, z. B. kein Passwort).
public class CapturingLoggerProvider : ILoggerProvider
{
    public ConcurrentQueue<(LogLevel Level, string Message)> Entries { get; } = new();

    public ILogger CreateLogger(string categoryName) => new CapturingLogger(this);

    public void Dispose()
    {
    }

    private sealed class CapturingLogger(CapturingLoggerProvider provider) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter) =>
            provider.Entries.Enqueue((logLevel, formatter(state, exception)));
    }
}
