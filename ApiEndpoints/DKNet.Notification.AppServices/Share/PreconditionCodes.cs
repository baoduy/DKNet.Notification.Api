namespace DKNet.Notification.AppServices.Share;

/// <summary>
/// Machine-readable codes a precondition validator names via <c>.WithErrorCode(...)</c>. Every code
/// starts with <see cref="Prefix" /> — the one signal <c>FluentValidationConfig</c>'s
/// <c>AddErrorResponses</c> setting uses to tell a refused precondition (409) from ordinary refused
/// input (400).
/// </summary>
public static class PreconditionCodes
{
    #region Fields

    public const string Prefix = "precondition.";

    #endregion
}
