namespace DKNet.Notification.Share.Extensions;

/// <summary>
///     Provides extension methods for sanitizing strings for logging purposes.
/// </summary>
public static class SanitizeForLoggingExtensions
{
    #region Methods

    /// <summary>
    ///     Sanitizes a string value by removing control characters and whitespace that could interfere with logging.
    /// </summary>
    /// <param name="value">The string value to sanitize.</param>
    /// <returns>A sanitized string safe for logging.</returns>
    public static string SanitizeForLogging(this string value) =>
        string.Concat(value.Where(c => !char.IsControl(c))).Trim();

    #endregion
}