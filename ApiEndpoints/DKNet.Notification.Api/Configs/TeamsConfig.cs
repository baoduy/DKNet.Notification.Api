using DKNet.Notification.AppServices.Delivery;

namespace DKNet.Notification.Api.Configs;

[ExcludeFromCodeCoverage]
internal static class TeamsConfig
{
    #region Methods

    /// <summary>
    ///     Reads the Teams settings once, here, and adds them with the Teams sender under its channel key. Bound with
    ///     <c>Get&lt;T&gt;()</c> on purpose: a later settings change has no effect until the next start. A bad Teams
    ///     setting never stops the start-up and writes no log entry: it only leaves Teams not configured.
    /// </summary>
    public static IServiceCollection AddTeamsConfig(this IServiceCollection services, IConfiguration configuration) =>
        services
            .AddSingleton(BindTeams(configuration.GetSection(TeamsChannelSettings.SectionName)))
            // Empty in the release, and bound to no setting: only a test host adds its test authority.
            .AddSingleton(new TeamsTrustedRoots([]))
            // Keyed, so the email sender stays the one plain IDeliverySender.
            .AddKeyedSingleton<IDeliverySender, TeamsWebhookSender>(TeamsWebhookSender.ChannelKey);

    // A value the binder cannot convert (an on/off value that is not true or false, a time limit that is not a whole
    // number) leaves Teams off, so not configured. The binder's own error holds the value, so it never leaves here.
    internal static TeamsChannelSettings BindTeams(IConfigurationSection section)
    {
        try
        {
            return section.Get<TeamsChannelSettings>() ?? new TeamsChannelSettings();
        }
        catch (InvalidOperationException)
        {
            return new TeamsChannelSettings();
        }
    }

    #endregion
}
