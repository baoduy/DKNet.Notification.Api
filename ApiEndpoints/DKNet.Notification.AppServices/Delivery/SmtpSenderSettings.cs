namespace DKNet.Notification.AppServices.Delivery;

/// <summary>The <c>Notifications:Email:Smtp</c> settings. No settings file holds <see cref="Password" />.</summary>
public sealed class SmtpSenderSettings
{
    #region Fields

    /// <summary>The security modes the sender takes; none of them is plain text.</summary>
    public static readonly IReadOnlyList<string> SecurityModes = ["StartTls", "Tls"];

    #endregion

    #region Properties

    /// <summary>Gets or sets the SMTP server host name: required, at most 255 characters.</summary>
    public string Host { get; set; } = string.Empty;

    /// <summary>Gets or sets the SMTP server port: 1 to 65535.</summary>
    public int Port { get; set; } = 587;

    /// <summary>Gets or sets the security mode: <c>StartTls</c> or <c>Tls</c>.</summary>
    public string Security { get; set; } = "StartTls";

    /// <summary>Gets or sets the sign-in user name, at most 256 characters; empty means no sign-in.</summary>
    public string UserName { get; set; } = string.Empty;

    /// <summary>
    ///     Gets or sets the sign-in password, at most 512 characters. Secret: an environment variable, Azure App
    ///     Configuration or user secrets only; never logged.
    /// </summary>
    public string Password { get; set; } = string.Empty;

    /// <summary>Gets or sets the sender address of every mail: required, <c>local@domain</c>, at most 254 characters.</summary>
    public string FromAddress { get; set; } = string.Empty;

    /// <summary>Gets or sets the sender display name, at most 100 characters.</summary>
    public string FromName { get; set; } = "DKNet Notification";

    #endregion
}
