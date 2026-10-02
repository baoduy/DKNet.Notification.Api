using DKNet.Notification.AppServices.Delivery;

namespace DKNet.Notification.Api.Configs;

[ExcludeFromCodeCoverage]
internal static partial class EmailConfig
{
    #region Methods

    /// <summary>
    ///     Reads the email, SMTP and delivery settings once, here. Bound with <c>Get&lt;T&gt;()</c> on purpose: a later
    ///     settings change has no effect until the next start. A bad delivery setting stops the start-up; bad email
    ///     settings only leave email not configured.
    /// </summary>
    public static IServiceCollection AddEmailConfig(this IServiceCollection services, IConfiguration configuration)
    {
        var delivery = configuration.GetSection(DeliverySettings.SectionName).Get<DeliverySettings>() ?? new DeliverySettings();
        delivery.Validate();
        var email = configuration.GetSection(EmailChannelSettings.SectionName).Get<EmailChannelSettings>()
                    ?? new EmailChannelSettings();

        return services
            .AddSingleton(delivery)
            .AddSingleton(email)
            .AddSingleton<DeliveryQueue>();
    }

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
