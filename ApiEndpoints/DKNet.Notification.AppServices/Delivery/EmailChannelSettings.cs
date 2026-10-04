using DKNet.Notification.Domains.Notifications;

namespace DKNet.Notification.AppServices.Delivery;

/// <summary>
///     The <c>Notifications:Email</c> settings, read once at start-up. Email is configured when it is on, the sender
///     is <c>Smtp</c> or <c>Graph</c> and every email setting and every setting of that sender keeps its rule;
///     otherwise every email call is skipped.
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

    /// <summary>The SMTP sender; its settings are the <see cref="Smtp" /> section.</summary>
    public const string SmtpSender = "Smtp";

    /// <summary>The Microsoft Graph sender; its settings are the <see cref="Graph" /> section.</summary>
    public const string GraphSender = "Graph";

    // Each sender's name is also the name of its settings section.
    private static readonly string[] Senders = [SmtpSender, GraphSender];

    private readonly List<string> _unconvertibleSettings = [];

    #endregion

    #region Properties

    /// <summary>Gets or sets whether email is on. The base settings keep it off.</summary>
    public bool Enabled { get; set; }

    /// <summary>Gets or sets the sender: <c>Smtp</c> or <c>Graph</c>, matched without case.</summary>
    public string Sender { get; set; } = SmtpSender;

    /// <summary>Gets or sets the time limit of each delivery attempt: 1 to 120 seconds.</summary>
    public int TimeoutSeconds { get; set; } = 30;

    /// <summary>Gets or sets the SMTP sender's settings, checked only when the sender is <c>Smtp</c>.</summary>
    public SmtpSenderSettings Smtp { get; set; } = new();

    /// <summary>Gets or sets the Graph sender's settings, checked only when the sender is <c>Graph</c>.</summary>
    public GraphSenderSettings Graph { get; set; } = new();

    /// <summary>
    ///     Gets the sender as its own name, <c>Smtp</c> or <c>Graph</c>, whatever the case of <see cref="Sender" />;
    ///     <see langword="null" /> for any other value.
    /// </summary>
    public string? ChosenSender =>
        Senders.FirstOrDefault(sender => string.Equals(sender, Sender, StringComparison.OrdinalIgnoreCase));

    #endregion

    #region Methods

    /// <summary>
    ///     Records a setting whose value the settings binder could not convert to its type: email is then not
    ///     configured. The setting keeps its default.
    /// </summary>
    /// <param name="key">The full key of the setting, never its value.</param>
    public void AddUnconvertibleSetting(string key) => _unconvertibleSettings.Add(key);

    /// <summary>
    ///     Checks the sender, then every unconvertible setting and every rule of the email settings and of the chosen
    ///     sender's settings. The other sender's settings are not checked. Whether email is on is not part of it.
    /// </summary>
    /// <returns>
    ///     The full key of each missing or bad setting (only the sender's key when the sender is neither <c>Smtp</c>
    ///     nor <c>Graph</c>); empty when email can send. Never a value.
    /// </returns>
    public IReadOnlyList<string> BadSettings()
    {
        var chosen = ChosenSender;
        if (chosen is null)
        {
            return [$"{SectionName}:{nameof(Sender)}"];
        }

        // An unconvertible setting of the other sender's section does not count.
        var otherSection = $"{SectionName}:{(chosen == GraphSender ? SmtpSender : GraphSender)}:";
        var unconvertible = _unconvertibleSettings.Where(key =>
            !$"{key}:".StartsWith(otherSection, StringComparison.OrdinalIgnoreCase));
        (bool IsBad, string Key)[] rules =
        [
            (TimeoutSeconds is < 1 or > 120, $"{SectionName}:{nameof(TimeoutSeconds)}"),
            .. chosen == GraphSender ? GraphRules() : SmtpRules()
        ];
        var broken = rules
            .Where(rule => rule.IsBad)
            .Select(rule => rule.Key);
        return [.. unconvertible, .. broken];
    }

    private (bool IsBad, string Key)[] SmtpRules()
    {
        var smtp = $"{SectionName}:{nameof(Smtp)}";
        return
        [
            (string.IsNullOrWhiteSpace(Smtp.Host) || Smtp.Host.Length > 255, $"{smtp}:{nameof(Smtp.Host)}"),
            (Smtp.Port is < 1 or > 65_535, $"{smtp}:{nameof(Smtp.Port)}"),
            (!SmtpSenderSettings.SecurityModes.Contains(Smtp.Security, StringComparer.OrdinalIgnoreCase),
                $"{smtp}:{nameof(Smtp.Security)}"),
            (Smtp.UserName.Length > 256, $"{smtp}:{nameof(Smtp.UserName)}"),
            (Smtp.Password.Length > 512, $"{smtp}:{nameof(Smtp.Password)}"),
            (!EmailRecipient.TryCreate(Smtp.FromAddress, out _), $"{smtp}:{nameof(Smtp.FromAddress)}"),
            (Smtp.FromName.Length > 100, $"{smtp}:{nameof(Smtp.FromName)}")
        ];
    }

    private (bool IsBad, string Key)[] GraphRules()
    {
        var graph = $"{SectionName}:{nameof(Graph)}";
        var usesSecret = string.Equals(
            Graph.Credential,
            GraphSenderSettings.ClientSecretCredential,
            StringComparison.OrdinalIgnoreCase);
        return
        [
            (!Guid.TryParse(Graph.TenantId, out _), $"{graph}:{nameof(Graph.TenantId)}"),
            (!Guid.TryParse(Graph.ClientId, out _), $"{graph}:{nameof(Graph.ClientId)}"),
            (!GraphSenderSettings.CredentialModes.Contains(Graph.Credential, StringComparer.OrdinalIgnoreCase),
                $"{graph}:{nameof(Graph.Credential)}"),
            (usesSecret && Graph.ClientSecret.Length is 0 or > 512, $"{graph}:{nameof(Graph.ClientSecret)}"),
            (!EmailRecipient.TryCreate(Graph.Mailbox, out _), $"{graph}:{nameof(Graph.Mailbox)}")
        ];
    }

    #endregion
}
