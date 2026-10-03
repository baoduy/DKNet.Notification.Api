namespace DKNet.Notification.AppServices.Delivery;

/// <summary>Makes one delivery attempt of a queued notification to its provider.</summary>
public interface IDeliverySender
{
    /// <summary>Submits the rendered message of <paramref name="notification" /> once, within the attempt's time limit.</summary>
    /// <param name="notification">A notification whose attempt is running.</param>
    /// <param name="stoppingToken">Cancelled when the host stops; the attempt then ends with no result.</param>
    /// <returns><see langword="null" /> when the provider accepted the message; otherwise why the attempt failed.</returns>
    Task<DeliveryFailure?> SendAsync(Domains.Notifications.Notification notification, CancellationToken stoppingToken);
}

/// <summary>Why a delivery attempt failed. It holds the provider's reply code only, never its reply text.</summary>
/// <param name="IsTransient">A timeout, a lost or refused connection, a TLS failure or an SMTP 4xx reply: worth another attempt.</param>
/// <param name="ReplyCode">The SMTP reply code; empty when there was no reply.</param>
/// <param name="RetryAfter">
///     The wait the provider asked for in its answer, used instead of the configured wait before the next attempt;
///     <see langword="null" /> keeps the configured wait.
/// </param>
public sealed record DeliveryFailure(bool IsTransient, string ReplyCode, TimeSpan? RetryAfter = null)
{
    /// <summary>Gets the failure kind as the log entries name it: <c>transient</c> or <c>permanent</c>.</summary>
    public string Kind => IsTransient ? "transient" : "permanent";
}
