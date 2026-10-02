using DKNet.Notification.Domains.Notifications;

namespace DKNet.Notification.AppServices.Delivery;

/// <summary>
///     The <c>Notifications:Email</c> settings, read once at start-up. Email is configured when it is on, the sender
///     is <c>Smtp</c> and every email and SMTP setting keeps its rule; otherwise every email call is skipped.
/// </summary>
/// <remarks>
///     <see cref="Sender" /> and <see cref="SmtpSenderSettings.Security" /> are text, not enums: binding a value
///     outside an enum throws, and a bad value must leave email "not configured" while the service still starts.
/// </remarks>
public sealed class EmailChannelSettings
{
    #region Fields

    /// <summary>The settings section.</summary>
    public const string SectionName = "Notifications:Email";

    /// <summary>The one sender this release has.</summary>
    public const string SmtpSender = "Smtp";

    private readonly List<string> _unconvertibleSettings = [];

    #endregion

    #region Properties

    /// <summary>Gets or sets whether email is on. The base settings keep it off.</summary>
    public bool Enabled { get; set; }

    /// <summary>Gets or sets the sender: only <c>Smtp</c> counts as configured.</summary>
    public string Sender { get; set; } = SmtpSender;

    /// <summary>Gets or sets the time limit of each delivery attempt: 1 to 120 seconds.</summary>
    public int TimeoutSeconds { get; set; } = 30;

    /// <summary>Gets or sets the SMTP sender's settings.</summary>
    public SmtpSenderSettings Smtp { get; set; } = new();

    #endregion

    #region Methods

    /// <summary>
    ///     Records a setting whose value the settings binder could not convert to its type: email is then not
    ///     configured. The setting keeps its default.
    /// </summary>
    /// <param name="key">The full key of the setting, never its value.</param>
    public void AddUnconvertibleSetting(string key) => _unconvertibleSettings.Add(key);

    /// <summary>
    ///     Checks the sender, then every unconvertible setting and every email and SMTP rule. Whether email is on is
    ///     not part of it.
    /// </summary>
    /// <returns>
    ///     The full key of each missing or bad setting (only the sender's key when the sender is not <c>Smtp</c>);
    ///     empty when email can send. Never a value.
    /// </returns>
    public IReadOnlyList<string> BadSettings()
    {
        if (!string.Equals(Sender, SmtpSender, StringComparison.OrdinalIgnoreCase))
        {
            return [$"{SectionName}:{nameof(Sender)}"];
        }

        var smtp = $"{SectionName}:{nameof(Smtp)}";
        var broken = new (bool IsBad, string Key)[]
            {
                (TimeoutSeconds is < 1 or > 120, $"{SectionName}:{nameof(TimeoutSeconds)}"),
                (string.IsNullOrWhiteSpace(Smtp.Host) || Smtp.Host.Length > 255, $"{smtp}:{nameof(Smtp.Host)}"),
                (Smtp.Port is < 1 or > 65_535, $"{smtp}:{nameof(Smtp.Port)}"),
                (!SmtpSenderSettings.SecurityModes.Contains(Smtp.Security, StringComparer.OrdinalIgnoreCase),
                    $"{smtp}:{nameof(Smtp.Security)}"),
                (Smtp.UserName.Length > 256, $"{smtp}:{nameof(Smtp.UserName)}"),
                (Smtp.Password.Length > 512, $"{smtp}:{nameof(Smtp.Password)}"),
                (!EmailRecipient.TryCreate(Smtp.FromAddress, out _), $"{smtp}:{nameof(Smtp.FromAddress)}"),
                (Smtp.FromName.Length > 100, $"{smtp}:{nameof(Smtp.FromName)}")
            }
            .Where(rule => rule.IsBad)
            .Select(rule => rule.Key);
        return [.. _unconvertibleSettings, .. broken];
    }

    #endregion
}
