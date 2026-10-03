using DKNet.Notification.AppServices.Delivery;
using DKNet.Notification.AppServices.Notifications;
using DKNet.Notification.AppServices.Templates;
using DKNet.Notification.Domains.Notifications;
using DKNet.Notification.Domains.Templates;
using Microsoft.Extensions.Logging;

namespace DKNet.Notification.App.Tests.Unit.Notifications;

/// <summary>DRK-2020 §3 steps 5, 6, 8 and 9 of an email call, with the log entry and count of each outcome.</summary>
public sealed class SendEmailServiceTests : IDisposable
{
    private const string AccountOpened = "account-opened";
    private const string TeamDigest = "team-digest";

    private static readonly ITemplateCatalogue Catalogue = new SendServiceHost.FixedCatalogue(
        new NotificationTemplate(AccountOpened, string.Empty, [
            new TemplateVersion("email", "account-opened.email.html", TemplateFormat.Html, "Your account is open",
                Title: null, "Dear {{customerName}}, your account {{accountNumber}} is open.")
        ]),
        new NotificationTemplate(TeamDigest, string.Empty, [
            new TemplateVersion("teams", "team-digest.teams.md", TemplateFormat.Markdown, Subject: null, "Team digest", "Digest")
        ]));

    private readonly SendServiceHost _host = new();

    public void Dispose() => _host.Dispose();

    private static EmailChannelSettings SetUp(bool enabled = true) => new()
    {
        Enabled = enabled,
        Smtp = new SmtpSenderSettings { Host = "smtp.example.com", FromAddress = "notifications@example.com" }
    };

    private SendNotificationService Service(EmailChannelSettings email, int queueCapacity = 10) =>
        _host.Service(Catalogue, email, new TeamsChannelSettings(), queueCapacity);

    private static SendNotificationRequest Email(string templateId, Dictionary<string, string> parameters) =>
        new("Email", templateId, parameters);

    private static Dictionary<string, string> Jane(string? to = "jane@example.com")
    {
        var parameters = new Dictionary<string, string>(StringComparer.Ordinal);
        if (to is not null)
        {
            parameters["to"] = to;
        }

        parameters["customerName"] = "Jane Tan";
        parameters["accountNumber"] = "0012345678";
        return parameters;
    }

    private void ShouldBeSkipped(Domains.Notifications.Notification notification, SkipReason reason)
    {
        notification.Status.ShouldBe(NotificationStatus.Skipped);
        notification.SkipReason.ShouldBe(reason);
        var entry = _host.Logs.Entries.ShouldHaveSingleItem();
        entry.EventId.Name.ShouldBe("NotificationSkipped");
        entry.Value("Reason").ShouldBe(reason.ToString());
        entry.Value("Channel").ShouldBe("email");
        _host.Measurements.ShouldHaveSingleItem().Tags.ShouldBe(new Dictionary<string, object?> { ["channel"] = "email", ["outcome"] = "skipped" });
    }

    private void ShouldBeRejected(Domains.Notifications.Notification notification, string code, string field)
    {
        notification.Status.ShouldBe(NotificationStatus.Rejected);
        notification.ErrorCode.ShouldBe(code);
        notification.ErrorField.ShouldBe(field);
        notification.Recipient.ShouldBeNull();
        notification.RenderedMessage.ShouldBeNull();
        var entry = _host.Logs.Entries.ShouldHaveSingleItem();
        entry.Level.ShouldBe(LogLevel.Information);
        entry.EventId.Name.ShouldBe("NotificationRejected");
        entry.Value("NotificationId").ShouldBe(notification.NotificationId.ToString());
        entry.Value("Code").ShouldBe(code);
        entry.Value("TemplateId").ShouldBe(AccountOpened);
        entry.Value("Channel").ShouldBe("email");
        entry.Value("CallerId").ShouldBe("treasury-ops");
        entry.Value("TraceId").ShouldBe("trace-1");
        var measurement = _host.Measurements.ShouldHaveSingleItem();
        measurement.Instrument.ShouldBe("notifications.rejected");
        measurement.Tags.ShouldBe(new Dictionary<string, object?> { ["code"] = code });
    }

    [Fact]
    public void A_valid_email_call_is_rendered_queued_logged_and_counted()
    {
        var notification = Service(SetUp()).Send(Email(AccountOpened, Jane()), "treasury-ops", "trace-1");

        notification.Status.ShouldBe(NotificationStatus.Queued);
        notification.Recipient.ShouldNotBeNull().Address.ShouldBe("jane@example.com");
        notification.RenderedMessage.ShouldBe(new RenderedMessage(
            "Your account is open",
            "Dear Jane Tan, your account 0012345678 is open.",
            BodyFormat.Html));
        var entry = _host.Logs.Entries.ShouldHaveSingleItem();
        entry.Level.ShouldBe(LogLevel.Information);
        entry.EventId.Id.ShouldBe(2003);
        entry.EventId.Name.ShouldBe("NotificationQueued");
        entry.Value("NotificationId").ShouldBe(notification.NotificationId.ToString());
        entry.Value("QueueLength").ShouldBe("1");
        entry.Value("TemplateId").ShouldBe(AccountOpened);
        entry.Value("Channel").ShouldBe("email");
        entry.Value("CallerId").ShouldBe("treasury-ops");
        entry.Value("TraceId").ShouldBe("trace-1");
        var measurement = _host.Measurements.ShouldHaveSingleItem();
        measurement.Instrument.ShouldBe("notifications.accepted");
        measurement.Tags.ShouldBe(new Dictionary<string, object?> { ["channel"] = "email", ["outcome"] = "queued" });
    }

    [Fact]
    public void An_email_call_with_email_off_is_skipped_before_its_recipient_is_checked() =>
        ShouldBeSkipped(
            Service(SetUp(enabled: false)).Send(Email(AccountOpened, Jane(to: null)), "treasury-ops", "trace-1"),
            SkipReason.ChannelNotConfigured);

    [Fact]
    public void An_email_call_with_email_on_but_not_set_up_is_skipped()
    {
        var email = SetUp();
        email.Sender = "Graph";

        ShouldBeSkipped(Service(email).Send(Email(AccountOpened, Jane()), "treasury-ops", "trace-1"), SkipReason.ChannelNotConfigured);
    }

    [Fact]
    public void An_email_call_to_a_template_with_no_email_version_is_skipped() =>
        ShouldBeSkipped(
            Service(SetUp()).Send(Email(TeamDigest, Jane(to: null)), "treasury-ops", "trace-1"),
            SkipReason.NoTemplateVersion);

    [Theory]
    [InlineData(null, "RECIPIENT_MISSING")]
    [InlineData("", "RECIPIENT_MISSING")]
    [InlineData(" jane@example.com", "RECIPIENT_INVALID")]
    [InlineData("Jane <jane@example.com>", "RECIPIENT_INVALID")]
    public void A_missing_or_bad_recipient_is_rejected_on_the_field_to(string? to, string code) =>
        ShouldBeRejected(Service(SetUp()).Send(Email(AccountOpened, Jane(to)), "treasury-ops", "trace-1"), code, "to");

    [Fact]
    public void An_address_a_mail_header_cannot_hold_is_rejected_on_the_field_to_and_never_queued()
    {
        var service = Service(SetUp());

        ShouldBeRejected(service.Send(Email(AccountOpened, Jane("jane.@example.com")), "treasury-ops", "trace-1"), "RECIPIENT_INVALID", "to");
        _host.Services.GetRequiredService<DeliveryQueue>().Length.ShouldBe(0);
    }

    [Fact]
    public void A_missing_parameter_is_rejected_on_its_parameter_field()
    {
        var parameters = Jane();
        parameters.Remove("accountNumber");

        ShouldBeRejected(
            Service(SetUp()).Send(Email(AccountOpened, parameters), "treasury-ops", "trace-1"),
            "PARAMETER_MISSING",
            "parameters.accountNumber");
    }

    [Fact]
    public void An_unknown_template_is_rejected_on_the_field_templateId()
    {
        var notification = Service(SetUp()).Send(Email("account-closed", Jane()), "treasury-ops", "trace-1");

        notification.Status.ShouldBe(NotificationStatus.Rejected);
        notification.ErrorCode.ShouldBe("TEMPLATE_NOT_FOUND");
        notification.ErrorField.ShouldBe("templateId");
    }

    [Fact]
    public void A_call_to_a_full_queue_is_rejected()
    {
        var service = Service(SetUp(), queueCapacity: 1);
        service.Send(Email(AccountOpened, Jane()), "treasury-ops", "trace-0").Status.ShouldBe(NotificationStatus.Queued);
        _host.Logs.Clear();
        _host.Measurements.Clear();

        ShouldBeRejected(service.Send(Email(AccountOpened, Jane()), "treasury-ops", "trace-1"), "QUEUE_FULL", string.Empty);
    }

    [Fact]
    public void No_entry_or_tag_holds_the_recipient_a_value_or_the_rendered_message()
    {
        var service = Service(SetUp(), queueCapacity: 1);
        service.Send(Email(AccountOpened, Jane()), "treasury-ops", "trace-1");
        service.Send(Email(AccountOpened, Jane()), "treasury-ops", "trace-2");
        service.Send(Email(AccountOpened, Jane("jane.example.com")), "treasury-ops", "trace-3");

        _host.Logs.Entries.Count.ShouldBe(3);
        string[] secrets = ["jane@example.com", "jane.example.com", "Jane Tan", "0012345678", "Your account is open"];
        _host.Logs.Entries.SelectMany(e => e.State.Select(p => Convert.ToString(p.Value)).Append(e.Message))
            .ShouldAllBe(text => text == null || !secrets.Any(secret => text.Contains(secret)));
        _host.Measurements.SelectMany(m => m.Tags.Values).ShouldAllBe(tag => !secrets.Contains(tag));
    }
}
