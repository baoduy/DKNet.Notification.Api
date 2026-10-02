namespace DKNet.Notification.AppServices.Notifications;

/// <summary>The codes a refused send call carries in <c>errors[].code</c>.</summary>
public static class NotificationErrorCodes
{
    #region Fields

    /// <summary>The body is not JSON, or breaks a field rule.</summary>
    public const string InvalidRequest = "INVALID_REQUEST";

    /// <summary>The template id names no registered template.</summary>
    public const string TemplateNotFound = "TEMPLATE_NOT_FOUND";

    #endregion
}
