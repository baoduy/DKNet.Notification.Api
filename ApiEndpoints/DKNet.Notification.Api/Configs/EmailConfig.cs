using DKNet.Notification.AppServices.Delivery;

namespace DKNet.Notification.Api.Configs;

[ExcludeFromCodeCoverage]
internal static partial class EmailConfig
{
    #region Methods

    /// <summary>
    ///     Reads the email, SMTP and delivery settings once, here, and adds the delivery queue, the SMTP sender and the
    ///     replica's one delivery worker. Bound with <c>Get&lt;T&gt;()</c> on purpose: a later settings change has no
    ///     effect until the next start. A bad delivery setting stops the start-up; bad email settings only leave email
    ///     not configured.
    /// </summary>
    public static IServiceCollection AddEmailConfig(this IServiceCollection services, IConfiguration configuration)
    {
        var delivery = BindDelivery(configuration.GetSection(DeliverySettings.SectionName));
        delivery.Validate();
        var email = BindEmail(configuration.GetSection(EmailChannelSettings.SectionName));

        return services
            .AddSingleton(delivery)
            .AddSingleton(email)
            .AddSingleton<DeliveryQueue>()
            // Empty in the release, and bound to no setting: only a test host adds its test authority.
            .AddSingleton(new SmtpTrustedRoots([]))
            .AddSingleton<IDeliverySender, SmtpEmailSender>()
            .AddSingleton<DeliveryWorker>()
            .AddHostedService<DeliveryWorkerHost>();
    }

    // The binder's own error holds the value, so it never leaves here: the refusal names the setting only.
    private static DeliverySettings BindDelivery(IConfigurationSection section)
    {
        try
        {
            return section.Get<DeliverySettings>() ?? new DeliverySettings();
        }
        catch (InvalidOperationException)
        {
            var key = UnconvertibleSettings<DeliverySettings>(section).FirstOrDefault() ?? section.Path;
            throw new InvalidOperationException($"The {key} setting must be a whole number.");
        }
    }

    // A value the binder cannot convert leaves email not configured, naming the setting; every other value is kept.
    private static EmailChannelSettings BindEmail(IConfigurationSection section)
    {
        try
        {
            return section.Get<EmailChannelSettings>() ?? new EmailChannelSettings();
        }
        catch (InvalidOperationException)
        {
            var unconvertible = UnconvertibleSettings<EmailChannelSettings>(section);
            var email = SettingsOf(section.AsEnumerable().Where(setting => !unconvertible.Contains(setting.Key)))
                            .GetSection(section.Path)
                            .Get<EmailChannelSettings>()
                        ?? new EmailChannelSettings();
            foreach (var key in unconvertible)
            {
                email.AddUnconvertibleSetting(key);
            }

            return email;
        }
    }

    /// <summary>The full key of each setting under <paramref name="section" /> the binder cannot convert on its own.</summary>
    private static string[] UnconvertibleSettings<T>(IConfigurationSection section) =>
        section.AsEnumerable()
            .Where(setting => setting.Value is not null && !Converts<T>(section.Path, setting))
            .Select(setting => setting.Key)
            .ToArray();

    private static bool Converts<T>(string sectionPath, KeyValuePair<string, string?> setting)
    {
        try
        {
            SettingsOf([setting]).GetSection(sectionPath).Get<T>();
            return true;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    private static IConfiguration SettingsOf(IEnumerable<KeyValuePair<string, string?>> settings) =>
        new ConfigurationBuilder().AddInMemoryCollection(settings).Build();

    /// <summary>
    ///     Writes the email start-up entry through the built host's logger: <c>EmailSenderStarted</c> when email is
    ///     configured, <c>EmailSenderNotConfigured</c> naming the bad settings when it is on but not configured, and
    ///     nothing when it is off.
    /// </summary>
    public static WebApplication UseEmailConfig(this WebApplication app)
    {
        var email = app.Services.GetRequiredService<EmailChannelSettings>();
        if (!email.Enabled)
        {
            return app;
        }

        var logger = app.Services.GetRequiredService<ILogger<EmailChannelSettings>>();
        var badSettings = email.BadSettings();
        if (badSettings.Count == 0)
        {
            logger.EmailSenderStarted(EmailChannelSettings.SmtpSender);
        }
        else
        {
            logger.EmailSenderNotConfigured(string.Join(", ", badSettings));
        }

        return app;
    }

    [LoggerMessage(
        EventId = 1001,
        EventName = "EmailSenderStarted",
        Level = LogLevel.Information,
        Message = "Email sender {Sender} started.")]
    private static partial void EmailSenderStarted(this ILogger logger, string sender);

    // Names the settings only, never a value: email calls are skipped until the settings are fixed and the
    // service restarts.
    [LoggerMessage(
        EventId = 1002,
        EventName = "EmailSenderNotConfigured",
        Level = LogLevel.Warning,
        Message = "Email is on but not set up, so every email call is skipped. Missing or bad settings: {Settings}.")]
    private static partial void EmailSenderNotConfigured(this ILogger logger, string settings);

    #endregion
}
