using System.Collections.Concurrent;
using System.Diagnostics;

namespace DKNet.Notification.App.BDDTests.Support;

/// <summary>
/// Records every activity of every source while a scenario runs, with all its data, so a step can check what a
/// trace holds. One per scenario: dispose it when the scenario ends.
/// </summary>
public sealed class TraceCapture : IDisposable
{
    private readonly ActivityListener _listener;
    private readonly ConcurrentQueue<Activity> _activities = new();

    public TraceCapture()
    {
        _listener = new ActivityListener
        {
            ShouldListenTo = _ => true,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = _activities.Enqueue
        };
        ActivitySource.AddActivityListener(_listener);
    }

    /// <summary>Every activity that ended so far.</summary>
    public IReadOnlyCollection<Activity> Activities => _activities.ToArray();

    /// <summary>Every text an activity holds: names, tags, events, links, baggage and status.</summary>
    public IReadOnlyList<string> AllText() =>
        Activities.SelectMany(a => new[] { a.Source.Name, a.OperationName, a.DisplayName, a.StatusDescription }
                .Concat(a.TagObjects.SelectMany(t => new[] { t.Key, Text(t.Value) }))
                .Concat(a.Baggage.SelectMany(b => new[] { b.Key, b.Value }))
                .Concat(a.Events.SelectMany(e => e.Tags.Select(t => Text(t.Value)).Prepend(e.Name)))
                .Concat(a.Links.SelectMany(l => l.Tags?.Select(t => Text(t.Value)) ?? [])))
            .OfType<string>()
            .ToArray();

    public void Dispose() => _listener.Dispose();

    private static string? Text(object? value) =>
        Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture);
}
