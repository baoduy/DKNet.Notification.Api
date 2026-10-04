using DKNet.Notification.AppServices.Delivery;
using DKNet.Notification.AppServices.Notifications;
using DKNet.Notification.Domains.Notifications;
using DKNet.Notification.Domains.Templates;
using Microsoft.Extensions.Logging;

namespace DKNet.Notification.App.Tests.Unit.Notifications;

/// <summary>
///     DRK-2035 §3 "The checks of a teams call" (brief DRK-2036 R1 to R3): each check in its order, the log entry and
///     count of each outcome, and nothing of the destination, a value or the URL in an entry or a tag.
/// </summary>
public sealed class SendTeamsServiceTests : IDisposable
{
    private const string StaffAccountOpened = "staff-account-opened";
    private const string AccountOpened = "account-opened";
    private const string StaffDigest = "staff-digest";
    private const string Webhook = "https://prod-00.westeurope.logic.azure.com/workflows/5d2c81f4a7/triggers/manual/paths/invoke?sig=Wb-s1gn-7731";

    /// <summary>The brief's §5 message with no title block and an empty text: the bytes every filled body adds to.</summary>
    private const string EmptyMessage =
        """{"type":"message","attachments":[{"contentType":"application/vnd.microsoft.card.adaptive","content":{"type":"AdaptiveCard","$schema":"http://adaptivecards.io/schemas/adaptive-card.json","version":"1.4","body":[{"type":"TextBlock","text":"","wrap":true}]}}]}""";

    private static readonly SendServiceHost.FixedCatalogue Catalogue = new(
        new NotificationTemplate(StaffAccountOpened, string.Empty, [
            new TemplateVersion("teams", "staff-account-opened.teams.md", TemplateFormat.Markdown, Subject: null,
                "Account {{accountNumber}} opened", "**{{customerName}}** opened account {{accountNumber}}.")
        ]),
        new NotificationTemplate(AccountOpened, string.Empty, [
            new TemplateVersion("email", "account-opened.email.html", TemplateFormat.Html, "Your account is open",
                Title: null, "Dear {{customerName}}")
        ]),
        new NotificationTemplate(StaffDigest, string.Empty, [
            new TemplateVersion("teams", "staff-digest.teams.md", TemplateFormat.Markdown, Subject: null, Title: null, "{{part1}}")
        ]));

    private readonly SendServiceHost _host = new();

    public void Dispose() => _host.Dispose();

    private static TeamsChannelSettings TeamsOn(params string[] destinations)
    {
        var teams = new TeamsChannelSettings { Enabled = true };
        foreach (var destination in destinations)
        {
            teams.Destinations[destination] = new TeamsDestination { WebhookUrl = Webhook };
        }

        return teams;
    }

    private SendNotificationService Service(TeamsChannelSettings teams, int queueCapacity = 10) =>
        _host.Service(
            Catalogue,
            new EmailChannelSettings
            {
                Enabled = true,
                Smtp = new SmtpSenderSettings { Host = "smtp.example.com", FromAddress = "notifications@example.com" }
            },
            teams,
            queueCapacity);

    private static SendNotificationRequest Teams(
        string? destination,
        string templateId = StaffAccountOpened,
        string channel = "teams",
        bool withAccount = true)
    {
        var parameters = new Dictionary<string, string>(StringComparer.Ordinal);
        if (destination is not null)
        {
            parameters["teamsDestination"] = destination;
        }

        parameters["customerName"] = "Jane Tan";
        if (withAccount)
        {
            parameters["accountNumber"] = "0012345678";
        }

        return new SendNotificationRequest(channel, templateId, parameters);
    }

    private static SendNotificationRequest Digest(int messageBytes) =>
        new("teams", StaffDigest, new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["teamsDestination"] = "ops-alerts",
            ["part1"] = new('x', messageBytes - EmptyMessage.Length)
        });

    private void ShouldBeSkipped(Domains.Notifications.Notification notification, SkipReason reason, string channel = "teams")
    {
        notification.Status.ShouldBe(NotificationStatus.Skipped);
        notification.SkipReason.ShouldBe(reason);
        notification.TeamsRecipient.ShouldBeNull();
        var entry = _host.Logs.Entries.ShouldHaveSingleItem();
        entry.Level.ShouldBe(LogLevel.Warning);
        entry.EventId.Name.ShouldBe("NotificationSkipped");
        entry.Value("Reason").ShouldBe(reason.ToString());
        entry.Value("Channel").ShouldBe(channel);
        _host.Measurements.ShouldHaveSingleItem().Tags
            .ShouldBe(new Dictionary<string, object?> { ["channel"] = channel, ["outcome"] = "skipped" });
    }

    private void ShouldBeRejected(Domains.Notifications.Notification notification, string code, string field)
    {
        notification.Status.ShouldBe(NotificationStatus.Rejected);
        notification.ErrorCode.ShouldBe(code);
        notification.ErrorField.ShouldBe(field);
        notification.TeamsRecipient.ShouldBeNull();
        notification.RenderedMessage.ShouldBeNull();
        var entry = _host.Logs.Entries.ShouldHaveSingleItem();
        entry.EventId.Name.ShouldBe("NotificationRejected");
        entry.Value("Code").ShouldBe(code);
        entry.Value("Channel").ShouldBe("teams");
        _host.Measurements.ShouldHaveSingleItem().Tags.ShouldBe(new Dictionary<string, object?> { ["code"] = code });
        _host.Services.GetRequiredService<DeliveryQueue>().Length.ShouldBe(0);
    }

    [Fact]
    public void A_valid_teams_call_is_rendered_queued_logged_and_counted()
    {
        var notification = Service(TeamsOn("ops-alerts")).Send(Teams("ops-alerts", channel: "Teams"), "treasury-ops", "trace-1");

        notification.Status.ShouldBe(NotificationStatus.Queued);
        notification.Channel.ShouldBe("teams");
        notification.TeamsRecipient.ShouldNotBeNull().Name.ShouldBe("ops-alerts");
        notification.Recipient.ShouldBeNull();
        notification.RenderedMessage.ShouldBe(new RenderedMessage(
            "Account 0012345678 opened",
            "**Jane Tan** opened account 0012345678.",
            BodyFormat.Markdown));
        _host.Services.GetRequiredService<DeliveryQueue>().Length.ShouldBe(1);
        var entry = _host.Logs.Entries.ShouldHaveSingleItem();
        entry.Level.ShouldBe(LogLevel.Information);
        entry.EventId.Name.ShouldBe("NotificationQueued");
        entry.Value("NotificationId").ShouldBe(notification.NotificationId.ToString());
        entry.Value("QueueLength").ShouldBe("1");
        entry.Value("TemplateId").ShouldBe(StaffAccountOpened);
        entry.Value("Channel").ShouldBe("teams");
        entry.Value("CallerId").ShouldBe("treasury-ops");
        entry.Value("TraceId").ShouldBe("trace-1");
        var measurement = _host.Measurements.ShouldHaveSingleItem();
        measurement.Instrument.ShouldBe("notifications.accepted");
        measurement.Tags.ShouldBe(new Dictionary<string, object?> { ["channel"] = "teams", ["outcome"] = "queued" });
    }

    [Fact]
    public void A_teams_call_with_Teams_off_is_skipped_before_its_template_version_is_checked() =>
        ShouldBeSkipped(
            Service(new TeamsChannelSettings()).Send(Teams(null, AccountOpened), "treasury-ops", "trace-1"),
            SkipReason.ChannelNotConfigured);

    [Fact]
    public void A_teams_call_with_a_bad_Teams_setting_is_skipped_as_not_configured()
    {
        var teams = TeamsOn("ops-alerts");
        teams.TimeoutSeconds = 0;

        ShouldBeSkipped(Service(teams).Send(Teams("ops-alerts"), "treasury-ops", "trace-1"), SkipReason.ChannelNotConfigured);
    }

    [Fact]
    public void Whether_Teams_is_configured_is_read_once_when_the_service_starts()
    {
        var teams = TeamsOn("ops-alerts");
        var service = Service(teams);
        teams.Enabled = false;

        service.Send(Teams("ops-alerts"), "treasury-ops", "trace-1").Status.ShouldBe(NotificationStatus.Queued);
    }

    [Fact]
    public void A_teams_call_to_a_template_with_no_teams_version_is_skipped_before_its_destination_is_checked() =>
        ShouldBeSkipped(
            Service(TeamsOn("ops-alerts")).Send(Teams(null, AccountOpened), "treasury-ops", "trace-1"),
            SkipReason.NoTemplateVersion);

    [Theory]
    [InlineData(null, "RECIPIENT_MISSING")]
    [InlineData("", "RECIPIENT_MISSING")]
    [InlineData("Ops-Alerts", "RECIPIENT_INVALID")]
    [InlineData(" ops-alerts", "RECIPIENT_INVALID")]
    [InlineData("ops_alerts", "RECIPIENT_INVALID")]
    public void A_missing_or_bad_destination_is_rejected_on_the_field_teamsDestination(string? destination, string code) =>
        ShouldBeRejected(
            Service(TeamsOn("ops-alerts")).Send(Teams(destination), "treasury-ops", "trace-1"),
            code,
            "teamsDestination");

    [Fact]
    public void A_destination_that_is_not_set_is_skipped_before_its_tokens_are_checked_and_never_logged()
    {
        var notification = Service(TeamsOn("ops-alerts")).Send(Teams("finance", withAccount: false), "treasury-ops", "trace-1");

        ShouldBeSkipped(notification, SkipReason.TeamsDestinationNotConfigured);
        ShouldHoldNone("finance", "Jane Tan", Webhook);
    }

    [Fact]
    public void Teams_on_with_no_destination_skips_every_destination() =>
        ShouldBeSkipped(
            Service(TeamsOn()).Send(Teams("ops-alerts"), "treasury-ops", "trace-1"),
            SkipReason.TeamsDestinationNotConfigured);

    [Fact]
    public void A_missing_parameter_is_rejected_on_its_parameter_field() =>
        ShouldBeRejected(
            Service(TeamsOn("ops-alerts")).Send(Teams("ops-alerts", withAccount: false), "treasury-ops", "trace-1"),
            "PARAMETER_MISSING",
            "parameters.accountNumber");

    [Fact]
    public void A_posted_message_of_28672_bytes_is_queued() =>
        Service(TeamsOn("ops-alerts")).Send(Digest(28_672), "treasury-ops", "trace-1").Status.ShouldBe(NotificationStatus.Queued);

    [Fact]
    public void A_posted_message_of_28673_bytes_is_rejected_naming_no_field() =>
        ShouldBeRejected(
            Service(TeamsOn("ops-alerts")).Send(Digest(28_673), "treasury-ops", "trace-1"),
            "MESSAGE_TOO_LARGE",
            string.Empty);

    [Fact]
    public void Email_and_Teams_share_the_places_of_the_queue()
    {
        var service = Service(TeamsOn("ops-alerts"), queueCapacity: 1);
        var email = new SendNotificationRequest("email", AccountOpened, new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["to"] = "jane@example.com",
            ["customerName"] = "Jane Tan"
        });
        service.Send(email, "treasury-ops", "trace-0").Status.ShouldBe(NotificationStatus.Queued);
        _host.Logs.Clear();
        _host.Measurements.Clear();

        var notification = service.Send(Teams("ops-alerts"), "treasury-ops", "trace-1");

        notification.Status.ShouldBe(NotificationStatus.Rejected);
        notification.ErrorCode.ShouldBe("QUEUE_FULL");
        notification.ErrorField.ShouldBe(string.Empty);
        notification.TeamsRecipient.ShouldBeNull();
        _host.Logs.Entries.ShouldHaveSingleItem().Value("Code").ShouldBe("QUEUE_FULL");
        _host.Services.GetRequiredService<DeliveryQueue>().Length.ShouldBe(1);
    }

    [Fact]
    public void Every_other_unknown_channel_is_still_not_supported() =>
        ShouldBeSkipped(
            Service(TeamsOn("ops-alerts")).Send(Teams("ops-alerts", channel: "whatsapp"), "treasury-ops", "trace-1"),
            SkipReason.ChannelNotSupported,
            "whatsapp");

    [Fact]
    public void No_entry_or_tag_holds_the_destination_a_value_the_title_or_the_url()
    {
        var service = Service(TeamsOn("ops-alerts"), queueCapacity: 1);
        service.Send(Teams("ops-alerts"), "treasury-ops", "trace-1");
        service.Send(Teams("ops-alerts"), "treasury-ops", "trace-2");
        service.Send(Teams("finance"), "treasury-ops", "trace-3");
        service.Send(Teams("Ops-Alerts"), "treasury-ops", "trace-4");

        _host.Logs.Entries.Count.ShouldBe(4);
        ShouldHoldNone("ops-alerts", "Ops-Alerts", "finance", "Jane Tan", "0012345678", "Account 0012345678 opened", Webhook, "Wb-s1gn-7731");
    }

    private void ShouldHoldNone(params string[] secrets)
    {
        _host.Logs.Entries.SelectMany(e => e.State.Select(p => Convert.ToString(p.Value)).Append(e.Message))
            .ShouldAllBe(text => text == null || !secrets.Any(secret => text.Contains(secret)));
        _host.Measurements.SelectMany(m => m.Tags.Values).ShouldAllBe(tag => !secrets.Contains(tag));
    }
}
