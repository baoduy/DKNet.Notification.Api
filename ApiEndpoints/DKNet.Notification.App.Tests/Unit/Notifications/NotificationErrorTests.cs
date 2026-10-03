using DKNet.Notification.Domains.Notifications;

namespace DKNet.Notification.App.Tests.Unit.Notifications;

/// <summary>DRK-2020 §3a: a rejected notification keeps the error it was refused with; a queued one needs both parts.</summary>
public sealed class NotificationErrorTests
{
    private static Domains.Notifications.Notification Received() =>
        Domains.Notifications.Notification.Receive(
            "account-opened",
            "email",
            new Dictionary<string, string>(StringComparer.Ordinal),
            "treasury-ops");

    [Fact]
    public void A_rejected_notification_keeps_its_error_code_and_field()
    {
        var notification = Received();
        notification.ErrorCode.ShouldBeNull();
        notification.ErrorField.ShouldBeNull();

        notification.Reject("RECIPIENT_MISSING", "to");

        notification.Status.ShouldBe(NotificationStatus.Rejected);
        notification.ErrorCode.ShouldBe("RECIPIENT_MISSING");
        notification.ErrorField.ShouldBe("to");
    }

    [Fact]
    public void A_queued_notification_cannot_be_rejected()
    {
        var notification = Received();
        EmailRecipient.TryCreate("jane@example.com", out var recipient).ShouldBeTrue();
        notification.Queue(recipient, new RenderedMessage("Your account is open", "Dear Jane", BodyFormat.Html));

        Should.Throw<InvalidOperationException>(() => notification.Reject("QUEUE_FULL", string.Empty))
            .Message.ShouldBe("A notification that is Queued cannot change its status.");
        notification.ErrorCode.ShouldBeNull();
    }

    [Fact]
    public void A_notification_is_not_queued_without_a_recipient_or_a_message()
    {
        var notification = Received();
        EmailRecipient.TryCreate("jane@example.com", out var recipient).ShouldBeTrue();

        Should.Throw<ArgumentNullException>(() =>
                notification.Queue((EmailRecipient)null!, new RenderedMessage("Your account is open", "Dear Jane", BodyFormat.Html)))
            .ParamName.ShouldBe("recipient");
        Should.Throw<ArgumentNullException>(() => notification.Queue(recipient, null!)).ParamName.ShouldBe("renderedMessage");
        notification.Status.ShouldBe(NotificationStatus.Received);
    }
}
