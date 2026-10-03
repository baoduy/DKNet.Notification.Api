using DKNet.Notification.Api.Configs;
using DKNet.Notification.AppServices.Delivery;
using Microsoft.Extensions.Configuration;

namespace DKNet.Notification.App.Tests.Unit.Delivery;

/// <summary>
///     DRK-2035 §3 "Teams settings and start-up" and §3a (brief DRK-2036 §6a D2 to D4): when Teams is configured, which
///     destination is set, and how the settings bind once at start-up.
/// </summary>
public sealed class TeamsChannelSettingsTests
{
    private const string Webhook = "https://prod-00.westeurope.logic.azure.com/workflows/5d2c81f4a7/triggers/manual/paths/invoke?sig=s1gn";

    private static TeamsChannelSettings On(int timeoutSeconds = 30, int destinations = 0)
    {
        var settings = new TeamsChannelSettings { Enabled = true, TimeoutSeconds = timeoutSeconds };
        for (var i = 1; i <= destinations; i++)
        {
            settings.Destinations[$"team-{i:000}"] = new TeamsDestination { WebhookUrl = Webhook };
        }

        return settings;
    }

    private static TeamsChannelSettings WithDestination(string name, string? webhookUrl)
    {
        var settings = On();
        settings.Destinations[name] = new TeamsDestination { WebhookUrl = webhookUrl };
        return settings;
    }

    /// <summary>An https URL of exactly <paramref name="length" /> characters.</summary>
    private static string HttpsUrlOfLength(int length)
    {
        const string prefix = "https://example.com/hook?sig=";
        return prefix + new string('s', length - prefix.Length);
    }

    private static TeamsChannelSettings Bind(Dictionary<string, string?> settings) =>
        TeamsConfig.BindTeams(new ConfigurationBuilder().AddInMemoryCollection(settings).Build().GetSection("Notifications:Teams"));

    #region D3 — IsConfigured

    [Fact]
    public void The_defaults_are_off_with_a_time_limit_of_30_seconds_and_no_destination()
    {
        var settings = new TeamsChannelSettings();

        TeamsChannelSettings.SectionName.ShouldBe("Notifications:Teams");
        settings.Enabled.ShouldBeFalse();
        settings.TimeoutSeconds.ShouldBe(30);
        settings.Destinations.ShouldBeEmpty();
        settings.IsConfigured.ShouldBeFalse();
    }

    [Theory]
    [InlineData(true, 1, 0, true)]
    [InlineData(true, 120, 0, true)]
    [InlineData(true, 30, 100, true)]
    [InlineData(false, 30, 1, false)]
    [InlineData(true, 0, 0, false)]
    [InlineData(true, 121, 0, false)]
    [InlineData(true, -1, 0, false)]
    [InlineData(true, 30, 101, false)]
    public void Teams_is_configured_only_when_on_with_a_time_limit_of_1_to_120_and_at_most_100_destinations(
        bool enabled,
        int timeoutSeconds,
        int destinations,
        bool configured)
    {
        var settings = On(timeoutSeconds, destinations);
        settings.Enabled = enabled;

        settings.IsConfigured.ShouldBe(configured);
    }

    #endregion

    #region D2 — WebhookFor

    [Fact]
    public void A_good_name_with_a_good_https_url_gives_that_url() =>
        WithDestination("ops-alerts", Webhook).WebhookFor("ops-alerts").ShouldBe(new Uri(Webhook));

    [Fact]
    public void A_name_is_matched_with_case()
    {
        var settings = WithDestination("ops-alerts", Webhook);

        settings.WebhookFor("Ops-Alerts").ShouldBeNull();
        settings.WebhookFor("ops-alerts").ShouldNotBeNull();
    }

    [Fact]
    public void A_configured_name_that_breaks_the_name_rule_is_not_set() =>
        WithDestination("Ops-Alerts", Webhook).WebhookFor("Ops-Alerts").ShouldBeNull();

    [Fact]
    public void A_name_with_no_destination_is_not_set() =>
        WithDestination("ops-alerts", Webhook).WebhookFor("finance").ShouldBeNull();

    [Fact]
    public void A_destination_with_no_settings_is_not_set()
    {
        var settings = On();
        settings.Destinations["ops-alerts"] = null!;

        settings.WebhookFor("ops-alerts").ShouldBeNull();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("https://")]
    [InlineData("http://127.0.0.1/hook")]
    [InlineData("hooks/x")]
    [InlineData("/hooks/x")]
    [InlineData("ftp://example.com/hook")]
    public void A_url_that_is_missing_or_not_an_absolute_https_url_is_not_set(string? webhookUrl) =>
        WithDestination("ops-alerts", webhookUrl).WebhookFor("ops-alerts").ShouldBeNull();

    [Fact]
    public void A_url_of_2048_characters_is_set_and_one_of_2049_is_not()
    {
        var longest = HttpsUrlOfLength(2_048);
        longest.Length.ShouldBe(2_048);

        WithDestination("ops-alerts", longest).WebhookFor("ops-alerts").ShouldBe(new Uri(longest));
        WithDestination("ops-alerts", HttpsUrlOfLength(2_049)).WebhookFor("ops-alerts").ShouldBeNull();
    }

    #endregion

    #region D4 — binding at start-up

    [Fact]
    public void Good_settings_bind_with_every_destination()
    {
        var settings = Bind(new Dictionary<string, string?>
        {
            ["Notifications:Teams:Enabled"] = "true",
            ["Notifications:Teams:TimeoutSeconds"] = "45",
            ["Notifications:Teams:Destinations:ops-alerts:WebhookUrl"] = Webhook
        });

        settings.Enabled.ShouldBeTrue();
        settings.TimeoutSeconds.ShouldBe(45);
        settings.IsConfigured.ShouldBeTrue();
        settings.WebhookFor("ops-alerts").ShouldBe(new Uri(Webhook));
    }

    [Fact]
    public void An_absent_section_binds_as_off()
    {
        var settings = Bind([]);

        settings.Enabled.ShouldBeFalse();
        settings.TimeoutSeconds.ShouldBe(30);
        settings.IsConfigured.ShouldBeFalse();
    }

    [Theory]
    [InlineData("Notifications:Teams:Enabled", "yes")]
    [InlineData("Notifications:Teams:TimeoutSeconds", "x")]
    public void A_value_the_binder_cannot_convert_leaves_Teams_not_configured_and_never_throws(string key, string value)
    {
        var settings = Bind(new Dictionary<string, string?>
        {
            ["Notifications:Teams:Enabled"] = "true",
            ["Notifications:Teams:Destinations:ops-alerts:WebhookUrl"] = Webhook,
            [key] = value
        });

        settings.IsConfigured.ShouldBeFalse();
    }

    [Fact]
    public void A_destination_named_with_no_url_is_not_set_and_the_others_still_work()
    {
        var settings = Bind(new Dictionary<string, string?>
        {
            ["Notifications:Teams:Enabled"] = "true",
            ["Notifications:Teams:Destinations:ops-alerts:WebhookUrl"] = Webhook,
            ["Notifications:Teams:Destinations:finance"] = string.Empty
        });

        settings.IsConfigured.ShouldBeTrue();
        settings.WebhookFor("finance").ShouldBeNull();
        settings.WebhookFor("ops-alerts").ShouldBe(new Uri(Webhook));
    }

    [Fact]
    public void A_destination_name_binds_with_its_case_and_is_matched_with_case()
    {
        var settings = Bind(new Dictionary<string, string?>
        {
            ["Notifications:Teams:Enabled"] = "true",
            ["Notifications:Teams:Destinations:Ops-Alerts:WebhookUrl"] = Webhook
        });

        settings.Destinations.Keys.ShouldBe(["Ops-Alerts"]);
        settings.WebhookFor("ops-alerts").ShouldBeNull();
        settings.WebhookFor("Ops-Alerts").ShouldBeNull();
    }

    #endregion
}
