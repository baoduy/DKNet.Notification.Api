namespace DKNet.Notification.AppServices.Notifications;

/// <summary>The codes a refused send call carries in <c>errors[].code</c>.</summary>
public static class NotificationErrorCodes
{
    #region Fields

    /// <summary>The body is not JSON, or breaks a field rule.</summary>
    public const string InvalidRequest = "INVALID_REQUEST";

    /// <summary>The template id names no registered template.</summary>
    public const string TemplateNotFound = "TEMPLATE_NOT_FOUND";

    /// <summary>The <c>to</c> parameter of an email call, or <c>teamsDestination</c> of a Teams call, is absent or empty.</summary>
    public const string RecipientMissing = "RECIPIENT_MISSING";

    /// <summary>The <c>to</c> parameter of an email call, or <c>teamsDestination</c> of a Teams call, breaks its rule.</summary>
    public const string RecipientInvalid = "RECIPIENT_INVALID";

    /// <summary>A template token has no parameter.</summary>
    public const string ParameterMissing = "PARAMETER_MISSING";

    /// <summary>The replica's delivery queue is full.</summary>
    public const string QueueFull = "QUEUE_FULL";

    /// <summary>The posted Teams message would be larger than 28,672 bytes.</summary>
    public const string MessageTooLarge = "MESSAGE_TOO_LARGE";

    #endregion
}
