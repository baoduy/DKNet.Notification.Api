using System.Diagnostics.CodeAnalysis;

namespace DKNet.Notification.Domains.Notifications;

/// <summary>
///     One call to send a notification. It lives in memory for the length of the call and is never stored.
/// </summary>
[SuppressMessage(
    "Naming",
    "CA1724:Type names should not match namespaces",
    Justification = "The design's aggregate name (docs/architect/02-domain.md); the clash is with the service's own root namespace.")]
public sealed class Notification
{
    #region Constructors

    [SuppressMessage(
        "Globalization",
        "CA1308:Normalize strings to uppercase",
        Justification = "DRK-2013: the channel is used in its lower-case form after step 5.")]
    private Notification(
        string templateId,
        string channel,
        IReadOnlyDictionary<string, string> parameters,
        string callerId)
    {
        NotificationId = Guid.CreateVersion7();
        TemplateId = templateId;
        Channel = channel.ToLowerInvariant();
        Parameters = parameters;
        CallerId = callerId;
        AcceptedAt = DateTimeOffset.UtcNow;
    }

    #endregion

    #region Properties

    /// <summary>Gets the id the caller gets back for an accepted call.</summary>
    public Guid NotificationId { get; }

    /// <summary>Gets the template id exactly as the caller named it.</summary>
    public string TemplateId { get; }

    /// <summary>Gets the channel in lower case: the caller's value is matched without case.</summary>
    public string Channel { get; }

    /// <summary>Gets the template parameters. Personal data: never logged.</summary>
    public IReadOnlyDictionary<string, string> Parameters { get; }

    /// <summary>Gets the calling application's id, or <c>System</c> with sign-in off.</summary>
    public string CallerId { get; }

    /// <summary>Gets when the call was received (UTC).</summary>
    public DateTimeOffset AcceptedAt { get; }

    /// <summary>Gets where the notification stands.</summary>
    public NotificationStatus Status { get; private set; } = NotificationStatus.Received;

    /// <summary>Gets why the notification is skipped; <see langword="null" /> unless <see cref="Status" /> is Skipped.</summary>
    public SkipReason? SkipReason { get; private set; }

    /// <summary>Gets the one address the email goes to; <see langword="null" /> until the notification is queued.</summary>
    public EmailRecipient? Recipient { get; private set; }

    /// <summary>Gets the filled subject and body; <see langword="null" /> until the notification is queued.</summary>
    public RenderedMessage? RenderedMessage { get; private set; }

    /// <summary>Gets how many delivery attempts were made: 0 to 3.</summary>
    public int AttemptCount { get; private set; }

    #endregion

    #region Methods

    /// <summary>Receives a call whose body passed its checks.</summary>
    /// <param name="templateId">The template id the caller named.</param>
    /// <param name="channel">The channel the caller named, in any case.</param>
    /// <param name="parameters">The template parameters.</param>
    /// <param name="callerId">The calling application's id.</param>
    /// <returns>A notification in <see cref="NotificationStatus.Received" />.</returns>
    public static Notification Receive(
        string templateId,
        string channel,
        IReadOnlyDictionary<string, string> parameters,
        string callerId) =>
        new(templateId, channel, parameters, callerId);

    /// <summary>Rejects the call: it named no registered template.</summary>
    public void Reject()
    {
        EnsureReceived();
        Status = NotificationStatus.Rejected;
    }

    /// <summary>Accepts the call without delivering it.</summary>
    /// <param name="reason">Why it is not delivered.</param>
    public void Skip(SkipReason reason)
    {
        EnsureReceived();
        Status = NotificationStatus.Skipped;
        SkipReason = reason;
    }

    /// <summary>Queues the call for delivery, rendered once, here, before it is queued.</summary>
    /// <param name="recipient">The one address the email goes to.</param>
    /// <param name="renderedMessage">The filled subject and body.</param>
    public void Queue(EmailRecipient recipient, RenderedMessage renderedMessage) =>
        throw new NotImplementedException();

    private void EnsureReceived()
    {
        if (Status != NotificationStatus.Received)
        {
            throw new InvalidOperationException($"A notification that is {Status} cannot change its status.");
        }
    }

    #endregion
}
