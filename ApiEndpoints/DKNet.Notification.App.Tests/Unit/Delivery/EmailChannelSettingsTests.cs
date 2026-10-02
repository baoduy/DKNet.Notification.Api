using DKNet.Notification.AppServices.Delivery;
using Microsoft.Extensions.Configuration;

namespace DKNet.Notification.App.Tests.Unit.Delivery;

/// <summary>DRK-2020 §3a: the email and SMTP settings rules, and the setting names a bad value is reported by.</summary>
public sealed class EmailChannelSettingsTests
{
    private static Dictionary<string, string?> SetUp() => new(StringComparer.Ordinal)
    {
        ["Notifications:Email:Enabled"] = "true",
        ["Notifications:Email:Sender"] = "Smtp",
        ["Notifications:Email:Smtp:Host"] = "smtp.example.com",
        ["Notifications:Email:Smtp:FromAddress"] = "notifications@example.com"
    };

    private static EmailChannelSettings Bind(Dictionary<string, string?> settings) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(settings)
            .Build()
            .GetSection("Notifications:Email")
            .Get<EmailChannelSettings>()
            .ShouldNotBeNull();

    [Fact]
    public void The_defaults_keep_email_off_with_the_spec_values()
    {
        var email = new EmailChannelSettings();

        email.Enabled.ShouldBeFalse();
        email.Sender.ShouldBe("Smtp");
        email.TimeoutSeconds.ShouldBe(30);
        email.Smtp.Host.ShouldBe(string.Empty);
        email.Smtp.Port.ShouldBe(587);
        email.Smtp.Security.ShouldBe("StartTls");
        email.Smtp.UserName.ShouldBe(string.Empty);
        email.Smtp.Password.ShouldBe(string.Empty);
        email.Smtp.FromAddress.ShouldBe(string.Empty);
        email.Smtp.FromName.ShouldBe("DKNet Notification");
    }

    [Fact]
    public void A_host_and_a_sender_address_set_email_up()
    {
        var email = Bind(SetUp());

        email.Enabled.ShouldBeTrue();
        email.BadSettings().ShouldBeEmpty();
    }

    [Theory]
    [InlineData("Notifications:Email:Sender", "smtp")]
    [InlineData("Notifications:Email:TimeoutSeconds", "1")]
    [InlineData("Notifications:Email:TimeoutSeconds", "120")]
    [InlineData("Notifications:Email:Smtp:Port", "1")]
    [InlineData("Notifications:Email:Smtp:Port", "65535")]
    [InlineData("Notifications:Email:Smtp:Security", "Tls")]
    [InlineData("Notifications:Email:Smtp:Security", "starttls")]
    public void A_setting_at_the_edge_of_its_rule_keeps_email_set_up(string key, string value)
    {
        var settings = SetUp();
        settings[key] = value;

        Bind(settings).BadSettings().ShouldBeEmpty();
    }

    [Theory]
    [InlineData(255, 256, 512, 100)]
    public void Texts_at_their_longest_keep_email_set_up(int host, int userName, int password, int fromName)
    {
        var settings = SetUp();
        settings["Notifications:Email:Smtp:Host"] = new string('h', host);
        settings["Notifications:Email:Smtp:UserName"] = new string('u', userName);
        settings["Notifications:Email:Smtp:Password"] = new string('p', password);
        settings["Notifications:Email:Smtp:FromName"] = new string('n', fromName);

        Bind(settings).BadSettings().ShouldBeEmpty();
    }

    [Theory]
    [InlineData("Notifications:Email:TimeoutSeconds", "0")]
    [InlineData("Notifications:Email:TimeoutSeconds", "121")]
    [InlineData("Notifications:Email:Smtp:Host", "")]
    [InlineData("Notifications:Email:Smtp:Host", "   ")]
    [InlineData("Notifications:Email:Smtp:Port", "0")]
    [InlineData("Notifications:Email:Smtp:Port", "65536")]
    [InlineData("Notifications:Email:Smtp:Security", "None")]
    [InlineData("Notifications:Email:Smtp:FromAddress", "")]
    [InlineData("Notifications:Email:Smtp:FromAddress", "DKNet <notifications@example.com>")]
    public void A_bad_value_is_reported_by_its_setting_name(string key, string value)
    {
        var settings = SetUp();
        settings[key] = value;

        Bind(settings).BadSettings().ShouldBe([key]);
    }

    [Theory]
    [InlineData("Notifications:Email:Smtp:Host", 256)]
    [InlineData("Notifications:Email:Smtp:UserName", 257)]
    [InlineData("Notifications:Email:Smtp:Password", 513)]
    [InlineData("Notifications:Email:Smtp:FromName", 101)]
    public void A_text_over_its_limit_is_reported_by_its_setting_name(string key, int length)
    {
        var settings = SetUp();
        settings[key] = new string('x', length);

        Bind(settings).BadSettings().ShouldBe([key]);
    }

    [Fact]
    public void Every_bad_setting_is_reported_in_order()
    {
        var settings = SetUp();
        settings["Notifications:Email:TimeoutSeconds"] = "500";
        settings.Remove("Notifications:Email:Smtp:Host");
        settings["Notifications:Email:Smtp:Port"] = "70000";
        settings.Remove("Notifications:Email:Smtp:FromAddress");

        Bind(settings).BadSettings().ShouldBe([
            "Notifications:Email:TimeoutSeconds",
            "Notifications:Email:Smtp:Host",
            "Notifications:Email:Smtp:Port",
            "Notifications:Email:Smtp:FromAddress"
        ]);
    }

    [Theory]
    [InlineData("Graph")]
    [InlineData("SendGrid")]
    public void A_sender_other_than_Smtp_is_the_only_setting_reported(string sender)
    {
        var settings = SetUp();
        settings["Notifications:Email:Sender"] = sender;
        settings.Remove("Notifications:Email:Smtp:Host");

        Bind(settings).BadSettings().ShouldBe(["Notifications:Email:Sender"]);
    }
}
