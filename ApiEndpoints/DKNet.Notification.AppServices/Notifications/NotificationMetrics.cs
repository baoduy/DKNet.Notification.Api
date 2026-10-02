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

    private readonly Counter<long> _accepted;
    private readonly Counter<long> _rejected;

    #endregion

    #region Constructors

    [SuppressMessage(
        "Reliability",
        "CA2000:Dispose objects before losing scope",
        Justification = "The IMeterFactory owns the meters it creates and disposes them with the host.")]
    public NotificationMetrics(IMeterFactory meterFactory)
    {
        var meter = meterFactory.Create(MeterName);
        _accepted = meter.CreateCounter<long>("notifications.accepted");
        _rejected = meter.CreateCounter<long>("notifications.rejected");
    }

    #endregion

    #region Methods

    /// <summary>Counts one accepted call.</summary>
    /// <param name="channel">The lower-case channel.</param>
    /// <param name="outcome">What became of the call, such as <c>skipped</c>.</param>
    public void Accepted(string channel, string outcome) =>
        _accepted.Add(1, new KeyValuePair<string, object?>("channel", channel), new KeyValuePair<string, object?>("outcome", outcome));

    /// <summary>Counts one refused call.</summary>
    /// <param name="code">The error code it was refused with.</param>
    public void Rejected(string code) => _rejected.Add(1, new KeyValuePair<string, object?>("code", code));

    #endregion
}
