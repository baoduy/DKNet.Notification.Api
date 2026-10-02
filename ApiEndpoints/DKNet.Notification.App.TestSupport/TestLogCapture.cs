using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace DKNet.Notification.App.TestSupport;

/// <summary>
/// Captures log lines written through <see cref="ILogger"/> during a scenario, so a step can assert on
/// observable log output instead of on internal call order. Registered as an additional <see cref="ILoggerProvider"/>
/// in a test host — it does not replace the console/other providers already configured.
/// </summary>
public sealed class TestLogCapture : ILoggerProvider
{
    private readonly ConcurrentQueue<string> _messages = new();

    public IReadOnlyCollection<string> Messages => _messages.ToArray();

    public void Clear() => _messages.Clear();

    public ILogger CreateLogger(string categoryName) => new CapturingLogger(_messages);

    public void Dispose()
    {
    }

    private sealed class CapturingLogger(ConcurrentQueue<string> sink) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter) =>
            sink.Enqueue(formatter(state, exception));
    }
}
