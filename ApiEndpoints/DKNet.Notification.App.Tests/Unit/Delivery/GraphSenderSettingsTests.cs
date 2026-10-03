using DKNet.Notification.AppServices.Delivery;
using Microsoft.Extensions.Configuration;

namespace DKNet.Notification.App.Tests.Unit.Delivery;

/// <summary>DRK-2028 §3a: the Graph settings rules, the sender choice and which settings each sender checks.</summary>
public sealed class GraphSenderSettingsTests
{
    private const string Graph = "Notifications:Email:Graph";

    /// <summary>64 + 1 + 63 + 1 + 63 + 1 + 57 + 4 = 254 characters, each label inside its own limit.</summary>
    private static readonly string Mailbox254 =
        $"{new string('a', 64)}@{new string('b', 63)}.{new string('c', 63)}.{new string('d', 57)}.com";

    private static Dictionary<string, string?> SetUp() => new(StringComparer.Ordinal)
    {
        ["Notifications:Email:Enabled"] = "true",
        ["Notifications:Email:Sender"] = "Graph",
        [$"{Graph}:TenantId"] = "3f2b9c1e-6a4d-4e0b-9d57-1c2f8a7e5b10",
        [$"{Graph}:ClientId"] = "7c1d4e2a-0b9f-4a63-8e15-2d6f9b3c8a41",
        [$"{Graph}:Mailbox"] = "notify@contoso.com"
    };

    private static EmailChannelSettings Bind(Dictionary<string, string?> settings) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(settings)
            .Build()
            .GetSection("Notifications:Email")
            .Get<EmailChannelSettings>()
            .ShouldNotBeNull();

    [Fact]
    public void The_Graph_defaults_are_empty_with_workload_identity()
    {
        var graph = new EmailChannelSettings().Graph;

        graph.TenantId.ShouldBe(string.Empty);
        graph.ClientId.ShouldBe(string.Empty);
        graph.Credential.ShouldBe("WorkloadIdentity");
        graph.ClientSecret.ShouldBe(string.Empty);
        graph.Mailbox.ShouldBe(string.Empty);
    }

    [Fact]
    public void A_tenant_a_client_and_a_mailbox_set_Graph_up_with_no_SMTP_setting()
    {
        var email = Bind(SetUp());

        email.Graph.TenantId.ShouldBe("3f2b9c1e-6a4d-4e0b-9d57-1c2f8a7e5b10");
        email.Graph.Mailbox.ShouldBe("notify@contoso.com");
        email.BadSettings().ShouldBeEmpty();
    }

    [Theory]
    [InlineData("Smtp", "Smtp")]
    [InlineData("smtp", "Smtp")]
    [InlineData("Graph", "Graph")]
    [InlineData("GRAPH", "Graph")]
    public void The_chosen_sender_is_the_sender_own_name_whatever_its_case(string sender, string chosen)
    {
        var email = new EmailChannelSettings { Sender = sender };

        email.ChosenSender.ShouldBe(chosen);
    }

    [Theory]
    [InlineData("SendGrid")]
    [InlineData("")]
    [InlineData("Graph ")]
    public void Any_other_sender_is_no_chosen_sender(string sender)
    {
        var email = new EmailChannelSettings { Sender = sender };

        email.ChosenSender.ShouldBeNull();
    }

    [Theory]
    [InlineData($"{Graph}:TenantId", "3F2B9C1E-6A4D-4E0B-9D57-1C2F8A7E5B10")]
    [InlineData($"{Graph}:TenantId", "3f2b9c1e6a4d4e0b9d571c2f8a7e5b10")]
    [InlineData($"{Graph}:TenantId", "{3f2b9c1e-6a4d-4e0b-9d57-1c2f8a7e5b10}")]
    [InlineData($"{Graph}:ClientId", "(7c1d4e2a-0b9f-4a63-8e15-2d6f9b3c8a41)")]
    [InlineData($"{Graph}:Credential", "WorkloadIdentity")]
    [InlineData($"{Graph}:Credential", "workloadidentity")]
    [InlineData($"{Graph}:ClientSecret", "")]
    public void A_Graph_setting_at_the_edge_of_its_rule_keeps_email_set_up(string key, string value)
    {
        var settings = SetUp();
        settings[key] = value;

        Bind(settings).BadSettings().ShouldBeEmpty();
    }

    [Fact]
    public void A_mailbox_of_254_characters_keeps_email_set_up()
    {
        Mailbox254.Length.ShouldBe(254);
        var settings = SetUp();
        settings[$"{Graph}:Mailbox"] = Mailbox254;

        Bind(settings).BadSettings().ShouldBeEmpty();
    }

    [Theory]
    [InlineData(1)]
    [InlineData(512)]
    public void A_client_secret_of_1_to_512_characters_sets_the_client_secret_mode_up(int length)
    {
        var settings = SetUp();
        settings[$"{Graph}:Credential"] = "clientsecret";
        settings[$"{Graph}:ClientSecret"] = new string('s', length);

        Bind(settings).BadSettings().ShouldBeEmpty();
    }

    [Fact]
    public void A_client_secret_of_513_characters_is_ignored_with_workload_identity()
    {
        var settings = SetUp();
        settings[$"{Graph}:Credential"] = "WorkloadIdentity";
        settings[$"{Graph}:ClientSecret"] = new string('s', 513);

        Bind(settings).BadSettings().ShouldBeEmpty();
    }

    [Theory]
    [InlineData($"{Graph}:TenantId", "")]
    [InlineData($"{Graph}:TenantId", "contoso.onmicrosoft.com")]
    [InlineData($"{Graph}:TenantId", "3f2b9c1e-6a4d-4e0b-9d57-1c2f8a7e5b1")]
    [InlineData($"{Graph}:ClientId", "")]
    [InlineData($"{Graph}:ClientId", "mail-sender")]
    [InlineData($"{Graph}:Credential", "Certificate")]
    [InlineData($"{Graph}:Credential", "")]
    [InlineData($"{Graph}:Mailbox", "")]
    [InlineData($"{Graph}:Mailbox", "notify.contoso.com")]
    [InlineData($"{Graph}:Mailbox", "notify@contoso.com, ops@contoso.com")]
    [InlineData("Notifications:Email:TimeoutSeconds", "0")]
    [InlineData("Notifications:Email:TimeoutSeconds", "121")]
    public void A_Graph_setting_outside_its_rule_is_the_one_setting_reported(string key, string value)
    {
        var settings = SetUp();
        settings[key] = value;

        Bind(settings).BadSettings().ShouldBe([key]);
    }

    [Fact]
    public void A_mailbox_of_255_characters_is_reported()
    {
        var settings = SetUp();
        settings[$"{Graph}:Mailbox"] = $"{new string('a', 64)}@{new string('b', 63)}.{new string('c', 63)}.{new string('d', 58)}.com";

        Bind(settings).BadSettings().ShouldBe([$"{Graph}:Mailbox"]);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(513)]
    public void A_missing_or_too_long_client_secret_is_reported_with_the_client_secret_mode(int length)
    {
        var settings = SetUp();
        settings[$"{Graph}:Credential"] = "ClientSecret";
        settings[$"{Graph}:ClientSecret"] = new string('s', length);

        Bind(settings).BadSettings().ShouldBe([$"{Graph}:ClientSecret"]);
    }

    [Fact]
    public void Every_bad_Graph_setting_is_reported_in_field_order_after_the_time_limit()
    {
        var settings = SetUp();
        settings["Notifications:Email:TimeoutSeconds"] = "0";
        settings[$"{Graph}:TenantId"] = "contoso.onmicrosoft.com";
        settings[$"{Graph}:ClientId"] = "mail-sender";
        settings[$"{Graph}:Credential"] = "Certificate";
        settings[$"{Graph}:Mailbox"] = "notify.contoso.com";

        Bind(settings).BadSettings().ShouldBe(
        [
            "Notifications:Email:TimeoutSeconds",
            $"{Graph}:TenantId",
            $"{Graph}:ClientId",
            $"{Graph}:Credential",
            $"{Graph}:Mailbox"
        ]);
    }

    [Fact]
    public void With_the_sender_Graph_no_SMTP_setting_is_checked()
    {
        var settings = SetUp();
        settings["Notifications:Email:Smtp:Host"] = new string('h', 256);
        settings["Notifications:Email:Smtp:Security"] = "None";
        settings["Notifications:Email:Smtp:FromAddress"] = "not-an-address";

        Bind(settings).BadSettings().ShouldBeEmpty();
    }

    [Fact]
    public void With_the_sender_Smtp_no_Graph_setting_is_checked()
    {
        var settings = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["Notifications:Email:Sender"] = "Smtp",
            ["Notifications:Email:Smtp:Host"] = "smtp.example.com",
            ["Notifications:Email:Smtp:FromAddress"] = "notifications@example.com",
            [$"{Graph}:TenantId"] = "contoso.onmicrosoft.com",
            [$"{Graph}:Credential"] = "Certificate",
            [$"{Graph}:Mailbox"] = "notify.contoso.com"
        };

        Bind(settings).BadSettings().ShouldBeEmpty();
    }

    [Fact]
    public void With_the_sender_Graph_an_unconvertible_SMTP_setting_is_not_reported()
    {
        var email = Bind(SetUp());

        email.AddUnconvertibleSetting("notifications:email:smtp:port");

        email.BadSettings().ShouldBeEmpty();
    }

    [Fact]
    public void With_the_sender_Graph_an_unconvertible_email_or_Graph_setting_is_reported()
    {
        var email = Bind(SetUp());

        email.AddUnconvertibleSetting("Notifications:Email:TimeoutSeconds");
        email.AddUnconvertibleSetting($"{Graph}:Mailbox");

        email.BadSettings().ShouldBe(["Notifications:Email:TimeoutSeconds", $"{Graph}:Mailbox"]);
    }

    [Fact]
    public void With_the_sender_Smtp_an_unconvertible_Graph_setting_is_not_reported_but_an_SMTP_one_is()
    {
        var email = Bind(new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["Notifications:Email:Sender"] = "Smtp",
            ["Notifications:Email:Smtp:Host"] = "smtp.example.com",
            ["Notifications:Email:Smtp:FromAddress"] = "notifications@example.com"
        });

        email.AddUnconvertibleSetting($"{Graph}:TenantId");
        email.AddUnconvertibleSetting("Notifications:Email:Smtp:Port");

        email.BadSettings().ShouldBe(["Notifications:Email:Smtp:Port"]);
    }
}
