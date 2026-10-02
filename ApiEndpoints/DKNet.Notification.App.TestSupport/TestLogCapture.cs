using System.Collections.Concurrent;
using System.Globalization;
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
    private readonly ConcurrentQueue<CapturedLogEntry> _entries = new();
    private readonly ConcurrentQueue<IReadOnlyList<KeyValuePair<string, object?>>> _scopes = new();

    public IReadOnlyCollection<string> Messages => _messages.ToArray();

    /// <summary>Every entry with its level, event, formatted text and structured state.</summary>
    public IReadOnlyCollection<CapturedLogEntry> Entries => _entries.ToArray();

    /// <summary>The state of every logging scope opened, so a check can search it too.</summary>
    public IReadOnlyCollection<IReadOnlyList<KeyValuePair<string, object?>>> Scopes => _scopes.ToArray();

    public void Clear()
    {
        _messages.Clear();
        _entries.Clear();
        _scopes.Clear();
    }

    public ILogger CreateLogger(string categoryName) => new CapturingLogger(categoryName, this);

    public void Dispose()
    {
    }

    private static IReadOnlyList<KeyValuePair<string, object?>> ToPairs<TState>(TState state) =>
        state is IEnumerable<KeyValuePair<string, object?>> pairs
            ? pairs.ToArray()
            : [new KeyValuePair<string, object?>("State", state)];

    private sealed class CapturingLogger(string category, TestLogCapture sink) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull
        {
            sink._scopes.Enqueue(ToPairs(state));
            return null;
        }

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            var message = formatter(state, exception);
            sink._messages.Enqueue(message);
            sink._entries.Enqueue(new CapturedLogEntry(category, logLevel, eventId, message, ToPairs(state), exception));
        }
    }
}

/// <summary>One captured log entry.</summary>
/// <param name="Category">The logger category.</param>
/// <param name="Level">The entry's level.</param>
/// <param name="EventId">The entry's event id; <see cref="EventId.Name" /> is the event name.</param>
/// <param name="Message">The formatted text.</param>
/// <param name="State">The structured state: the template's named values, plus <c>{OriginalFormat}</c>.</param>
/// <param name="Exception">The exception logged with the entry, if any.</param>
public sealed record CapturedLogEntry(
    string Category,
    LogLevel Level,
    EventId EventId,
    string Message,
    IReadOnlyList<KeyValuePair<string, object?>> State,
    Exception? Exception)
{
    /// <summary>The invariant text of the state value named <paramref name="key" /> (ordinal match), or null.</summary>
    public string? Value(string key) =>
        State.Where(p => string.Equals(p.Key, key, StringComparison.Ordinal))
            .Select(p => Convert.ToString(p.Value, CultureInfo.InvariantCulture))
            .FirstOrDefault();
}
