using DKNet.Notification.Domains.Notifications;

namespace DKNet.Notification.App.Tests.Unit.Notifications;

/// <summary>
/// DRK-2013 §3a: the memory-only Notification and its Received → Rejected | Skipped steps. DRK-2020 §3a: the
/// Received → Queued step, with its recipient, rendered message and attempt count.
/// </summary>
public sealed class NotificationTests
{
    private static readonly Dictionary<string, string> Parameters = new(StringComparer.Ordinal) { ["to"] = "jane@example.com" };

    private static Domains.Notifications.Notification Received() =>
        Domains.Notifications.Notification.Receive("account-opened", "Teams", Parameters, "treasury-ops");

    [Fact]
    public void A_received_notification_keeps_the_call_with_the_channel_in_lower_case()
    {
        var before = DateTimeOffset.UtcNow;

        var notification = Received();

        notification.TemplateId.ShouldBe("account-opened");
        notification.Channel.ShouldBe("teams");
        notification.Parameters.ShouldBeSameAs(Parameters);
        notification.CallerId.ShouldBe("treasury-ops");
        notification.Status.ShouldBe(NotificationStatus.Received);
        notification.SkipReason.ShouldBeNull();
        notification.NotificationId.Version.ShouldBe(7);
        notification.AcceptedAt.ShouldBeInRange(before, DateTimeOffset.UtcNow);
    }

    [Fact]
    public void Each_notification_gets_its_own_id() =>
        Received().NotificationId.ShouldNotBe(Received().NotificationId);

    [Fact]
    public void A_skipped_notification_names_its_reason()
    {
        var notification = Received();

        notification.Skip(SkipReason.ChannelNotSupported);

        notification.Status.ShouldBe(NotificationStatus.Skipped);
        notification.SkipReason.ShouldBe(SkipReason.ChannelNotSupported);
    }

    [Fact]
    public void A_rejected_notification_has_no_skip_reason()
    {
        var notification = Received();

        notification.Reject();

        notification.Status.ShouldBe(NotificationStatus.Rejected);
        notification.SkipReason.ShouldBeNull();
    }

    [Fact]
    public void A_skipped_notification_cannot_be_rejected()
    {
        var notification = Received();
        notification.Skip(SkipReason.ChannelNotSupported);

        Should.Throw<InvalidOperationException>(notification.Reject)
            .Message.ShouldBe("A notification that is Skipped cannot change its status.");
        notification.Status.ShouldBe(NotificationStatus.Skipped);
    }

    [Fact]
    public void A_rejected_notification_cannot_be_skipped()
    {
        var notification = Received();
        notification.Reject();

        Should.Throw<InvalidOperationException>(() => notification.Skip(SkipReason.ChannelNotSupported))
            .Message.ShouldBe("A notification that is Rejected cannot change its status.");
        notification.SkipReason.ShouldBeNull();
    }

    [Fact]
    public void A_queued_notification_keeps_its_recipient_and_rendered_message()
    {
        var notification = Received();
        EmailRecipient.TryCreate("jane@example.com", out var recipient).ShouldBeTrue();
        var message = new RenderedMessage("Your account is open", "Dear Jane Tan, your account 0012345678 is open.", BodyFormat.Html);

        notification.Queue(recipient, message);

        notification.Status.ShouldBe(NotificationStatus.Queued);
        notification.Recipient.ShouldNotBeNull().Address.ShouldBe("jane@example.com");
        notification.RenderedMessage.ShouldBeSameAs(message);
        notification.SkipReason.ShouldBeNull();
        notification.AttemptCount.ShouldBe(0);
    }

    [Fact]
    public void A_skipped_notification_cannot_be_queued()
    {
        var notification = Received();
        notification.Skip(SkipReason.ChannelNotConfigured);
        EmailRecipient.TryCreate("jane@example.com", out var recipient).ShouldBeTrue();

        Should.Throw<InvalidOperationException>(() =>
                notification.Queue(recipient, new RenderedMessage("Your account is open", "Dear Jane", BodyFormat.Html)))
            .Message.ShouldBe("A notification that is Skipped cannot change its status.");
        notification.Status.ShouldBe(NotificationStatus.Skipped);
        notification.Recipient.ShouldBeNull();
        notification.RenderedMessage.ShouldBeNull();
    }

    [Fact]
    public void A_queued_notification_cannot_be_skipped()
    {
        var notification = Received();
        EmailRecipient.TryCreate("jane@example.com", out var recipient).ShouldBeTrue();
        notification.Queue(recipient, new RenderedMessage("Your account is open", "Dear Jane", BodyFormat.Html));

        Should.Throw<InvalidOperationException>(() => notification.Skip(SkipReason.ChannelNotSupported))
            .Message.ShouldBe("A notification that is Queued cannot change its status.");
        notification.SkipReason.ShouldBeNull();
    }
}
