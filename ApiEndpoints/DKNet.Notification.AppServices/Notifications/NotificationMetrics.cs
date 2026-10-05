using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.Metrics;

namespace DKNet.Notification.AppServices.Notifications;

/// <summary>
///     The notification counters, on the meter <see cref="MeterName" />. No tag holds a parameter value or a
///     recipient.
/// </summary>
public sealed class NotificationMetrics
{
    #region Fields

    /// <summary>The meter the counters are on; the OpenTelemetry set-up exports it.</summary>
    public const string MeterName = "DKNet.Notification";

    /// <summary>The activity source of the delivery attempts, named like the meter; the OpenTelemetry set-up exports it.</summary>
    public const string DeliveryActivitySourceName = "DKNet.Notification";

    private readonly Meter _meter;
    private readonly Counter<long> _accepted;
    private readonly Counter<long> _rejected;
    private readonly Counter<long> _delivered;
    private readonly Counter<long> _failed;
    private readonly Histogram<double> _deliveryDuration;

    #endregion

    #region Constructors

    [SuppressMessage(
        "Reliability",
        "CA2000:Dispose objects before losing scope",
        Justification = "The IMeterFactory owns the meters it creates and disposes them with the host.")]
    public NotificationMetrics(IMeterFactory meterFactory)
    {
        _meter = meterFactory.Create(MeterName);
        _accepted = _meter.CreateCounter<long>("notifications.accepted");
        _rejected = _meter.CreateCounter<long>("notifications.rejected");
        _delivered = _meter.CreateCounter<long>("notifications.delivered");
        _failed = _meter.CreateCounter<long>("notifications.failed");
        _deliveryDuration = _meter.CreateHistogram<double>("notifications.delivery.duration", unit: "s");
    }

    #endregion

    #region Methods

    /// <summary>Counts one accepted call.</summary>
    /// <param name="channel">The lower-case channel.</param>
    /// <param name="outcome">What became of the call: <c>queued</c> or <c>skipped</c>.</param>
    public void Accepted(string channel, string outcome) =>
        _accepted.Add(1, new KeyValuePair<string, object?>("channel", channel), new KeyValuePair<string, object?>("outcome", outcome));

    /// <summary>Counts one refused call.</summary>
    /// <param name="code">The error code it was refused with.</param>
    public void Rejected(string code) => _rejected.Add(1, new KeyValuePair<string, object?>("code", code));

    /// <summary>Counts one delivered notification and records how long it took from acceptance to delivery.</summary>
    /// <param name="channel">The lower-case channel.</param>
    /// <param name="duration">The time from acceptance to delivery.</param>
    public void Delivered(string channel, TimeSpan duration)
    {
        var tag = new KeyValuePair<string, object?>("channel", channel);
        _delivered.Add(1, tag);
        _deliveryDuration.Record(duration.TotalSeconds, tag);
    }

    /// <summary>Counts one notification that ended Failed.</summary>
    /// <param name="channel">The lower-case channel.</param>
    public void Failed(string channel) => _failed.Add(1, new KeyValuePair<string, object?>("channel", channel));

    /// <summary>Shows the replica's queue length as the gauge <c>notifications.queue.length</c>.</summary>
    /// <param name="length">Reads how many notifications in the replica have not ended.</param>
    public void ObserveQueueLength(Func<int> length) => _meter.CreateObservableGauge("notifications.queue.length", length);

    #endregion
}
