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
    #region Fields

    /// <summary>The delivery attempts a notification gets at most, whatever the settings say.</summary>
    public const int MaxAttempts = 3;

    #endregion

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

    /// <summary>Gets the error code the call was refused with; <see langword="null" /> unless <see cref="Status" /> is Rejected.</summary>
    public string? ErrorCode { get; private set; }

    /// <summary>Gets the request field at fault, as the error body names it; <see langword="null" /> unless Rejected.</summary>
    public string? ErrorField { get; private set; }

    /// <summary>Gets the one address the email goes to; <see langword="null" /> unless an email notification is queued.</summary>
    public EmailRecipient? Recipient { get; private set; }

    /// <summary>Gets the Teams destination the card goes to; <see langword="null" /> unless a Teams notification is queued.</summary>
    public TeamsRecipient? TeamsRecipient { get; private set; }

    /// <summary>Gets the filled subject or title and the body; <see langword="null" /> until the notification is queued.</summary>
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

    /// <summary>Rejects the call: nothing is queued.</summary>
    public void Reject()
    {
        EnsureReceived();
        Status = NotificationStatus.Rejected;
    }

    /// <summary>Rejects the call with the error it is refused with: nothing is queued.</summary>
    /// <param name="errorCode">The error code the call is refused with.</param>
    /// <param name="errorField">The request field at fault, as the error body names it.</param>
    public void Reject(string errorCode, string errorField)
    {
        Reject();
        ErrorCode = errorCode;
        ErrorField = errorField;
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
    public void Queue(EmailRecipient recipient, RenderedMessage renderedMessage)
    {
        ArgumentNullException.ThrowIfNull(recipient);
        MarkQueued(renderedMessage);
        Recipient = recipient;
    }

    /// <summary>Queues a Teams call for delivery, rendered once, here, before it is queued.</summary>
    /// <param name="recipient">The Teams destination the card goes to.</param>
    /// <param name="renderedMessage">The filled title and Markdown body.</param>
    public void Queue(TeamsRecipient recipient, RenderedMessage renderedMessage)
    {
        ArgumentNullException.ThrowIfNull(recipient);
        MarkQueued(renderedMessage);
        TeamsRecipient = recipient;
    }

    /// <summary>Starts a delivery attempt: Queued or RetryWaiting to Delivering, one more attempt, never more than 3.</summary>
    public void StartAttempt()
    {
        if (Status is not (NotificationStatus.Queued or NotificationStatus.RetryWaiting))
        {
            throw new InvalidOperationException($"A notification that is {Status} cannot start a delivery attempt.");
        }

        if (AttemptCount >= MaxAttempts)
        {
            throw new InvalidOperationException($"A notification never gets more than {MaxAttempts} delivery attempts.");
        }

        Status = NotificationStatus.Delivering;
        AttemptCount++;
    }

    /// <summary>Ends the running attempt Delivered: the provider accepted the message.</summary>
    public void Deliver() => EndAttempt(NotificationStatus.Delivered);

    /// <summary>Ends the running attempt with a transient failure: the notification waits for its next attempt.</summary>
    public void WaitForRetry() => EndAttempt(NotificationStatus.RetryWaiting);

    /// <summary>Ends the running attempt Failed: a permanent failure, or a transient failure on the last attempt.</summary>
    public void Fail() => EndAttempt(NotificationStatus.Failed);

    private void EndAttempt(NotificationStatus end)
    {
        if (Status != NotificationStatus.Delivering)
        {
            throw new InvalidOperationException($"A notification that is {Status} has no delivery attempt to end.");
        }

        Status = end;
    }

    private void MarkQueued(RenderedMessage renderedMessage)
    {
        ArgumentNullException.ThrowIfNull(renderedMessage);
        EnsureReceived();
        Status = NotificationStatus.Queued;
        RenderedMessage = renderedMessage;
    }

    private void EnsureReceived()
    {
        if (Status != NotificationStatus.Received)
        {
            throw new InvalidOperationException($"A notification that is {Status} cannot change its status.");
        }
    }

    #endregion
}
