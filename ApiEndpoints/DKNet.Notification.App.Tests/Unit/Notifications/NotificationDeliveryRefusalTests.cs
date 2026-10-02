using DKNet.Notification.Domains.Notifications;

namespace DKNet.Notification.App.Tests.Unit.Notifications;

/// <summary>DRK-2020 §3a: what a refused delivery step says, so a bug in the worker reads plainly in its error.</summary>
public sealed class NotificationDeliveryRefusalTests
{
    private static Domains.Notifications.Notification Queued()
    {
        var notification = Domains.Notifications.Notification.Receive(
            "account-opened",
            "email",
            new Dictionary<string, string>(StringComparer.Ordinal) { ["to"] = "jane@example.com" },
            "treasury-ops");
        EmailRecipient.TryCreate("jane@example.com", out var recipient).ShouldBeTrue();
        notification.Queue(recipient, new RenderedMessage("Your account is open", "Dear Jane", BodyFormat.Html));
        return notification;
    }

    [Fact]
    public void A_running_attempt_cannot_start_another()
    {
        var notification = Queued();
        notification.StartAttempt();

        Should.Throw<InvalidOperationException>(notification.StartAttempt)
            .Message.ShouldBe("A notification that is Delivering cannot start a delivery attempt.");
    }

    [Fact]
    public void A_4th_attempt_is_refused()
    {
        var notification = Queued();
        for (var attempt = 1; attempt <= Domains.Notifications.Notification.MaxAttempts; attempt++)
        {
            notification.StartAttempt();
            notification.WaitForRetry();
        }

        Should.Throw<InvalidOperationException>(notification.StartAttempt)
            .Message.ShouldBe("A notification never gets more than 3 delivery attempts.");
    }

    [Fact]
    public void A_queued_notification_has_no_attempt_to_end()
    {
        Should.Throw<InvalidOperationException>(Queued().Deliver)
            .Message.ShouldBe("A notification that is Queued has no delivery attempt to end.");
    }
}
