using DKNet.Notification.Domains.Notifications;

namespace DKNet.Notification.App.Tests.Unit.Notifications;

/// <summary>
/// DRK-2013 §3a: the memory-only Notification and its Received → Rejected | Skipped steps. DRK-2020 §3a: the
/// Received → Queued step, with its recipient, rendered message and attempt count, and the delivery steps
/// Queued | RetryWaiting → Delivering → Delivered | RetryWaiting | Failed, never more than 3 attempts (brief
/// DRK-2023 §3 row 1).
/// </summary>
public sealed class NotificationTests
{
    private static readonly Dictionary<string, string> Parameters = new(StringComparer.Ordinal) { ["to"] = "jane@example.com" };
    private static readonly DateTimeOffset AcceptedAt = new(2026, 10, 5, 9, 30, 0, TimeSpan.Zero);

    private static Domains.Notifications.Notification Received() =>
        Domains.Notifications.Notification.Receive("account-opened", "Teams", Parameters, "treasury-ops", AcceptedAt);

    [Fact]
    public void A_received_notification_keeps_the_call_with_the_channel_in_lower_case()
    {
        var notification = Received();

        notification.TemplateId.ShouldBe("account-opened");
        notification.Channel.ShouldBe("teams");
        notification.Parameters.ShouldBeSameAs(Parameters);
        notification.CallerId.ShouldBe("treasury-ops");
        notification.Status.ShouldBe(NotificationStatus.Received);
        notification.SkipReason.ShouldBeNull();
        notification.NotificationId.Version.ShouldBe(7);
        notification.AcceptedAt.ShouldBe(AcceptedAt);
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

    private static Domains.Notifications.Notification Queued()
    {
        var notification = Received();
        EmailRecipient.TryCreate("jane@example.com", out var recipient).ShouldBeTrue();
        notification.Queue(recipient, new RenderedMessage("Your account is open", "Dear Jane", BodyFormat.Html));
        return notification;
    }

    [Fact]
    public void A_queued_notification_starts_attempt_1()
    {
        var notification = Queued();

        notification.StartAttempt();

        notification.Status.ShouldBe(NotificationStatus.Delivering);
        notification.AttemptCount.ShouldBe(1);
    }

    [Fact]
    public void An_attempt_the_provider_accepts_ends_the_notification_delivered()
    {
        var notification = Queued();
        notification.StartAttempt();

        notification.Deliver();

        notification.Status.ShouldBe(NotificationStatus.Delivered);
        notification.AttemptCount.ShouldBe(1);
    }

    [Fact]
    public void A_transient_failure_makes_the_notification_wait_for_its_next_attempt()
    {
        var notification = Queued();
        notification.StartAttempt();

        notification.WaitForRetry();

        notification.Status.ShouldBe(NotificationStatus.RetryWaiting);
        notification.AttemptCount.ShouldBe(1);
    }

    [Fact]
    public void A_waiting_notification_starts_its_next_attempt()
    {
        var notification = Queued();
        notification.StartAttempt();
        notification.WaitForRetry();

        notification.StartAttempt();

        notification.Status.ShouldBe(NotificationStatus.Delivering);
        notification.AttemptCount.ShouldBe(2);
    }

    [Fact]
    public void A_failed_attempt_can_end_the_notification_failed()
    {
        var notification = Queued();
        notification.StartAttempt();

        notification.Fail();

        notification.Status.ShouldBe(NotificationStatus.Failed);
        notification.AttemptCount.ShouldBe(1);
    }

    [Fact]
    public void A_notification_never_gets_a_4th_attempt()
    {
        var notification = Queued();
        for (var attempt = 1; attempt <= 3; attempt++)
        {
            notification.StartAttempt();
            notification.AttemptCount.ShouldBe(attempt);
            notification.WaitForRetry();
        }

        Should.Throw<InvalidOperationException>(notification.StartAttempt);
        notification.AttemptCount.ShouldBe(3);
        notification.Status.ShouldBe(NotificationStatus.RetryWaiting);
    }

    [Fact]
    public void A_received_notification_cannot_start_an_attempt()
    {
        var notification = Received();

        Should.Throw<InvalidOperationException>(notification.StartAttempt);
        notification.Status.ShouldBe(NotificationStatus.Received);
        notification.AttemptCount.ShouldBe(0);
    }

    [Fact]
    public void A_delivering_notification_cannot_start_another_attempt()
    {
        var notification = Queued();
        notification.StartAttempt();

        Should.Throw<InvalidOperationException>(notification.StartAttempt);
        notification.Status.ShouldBe(NotificationStatus.Delivering);
        notification.AttemptCount.ShouldBe(1);
    }

    [Theory]
    [InlineData(NotificationStatus.Delivered)]
    [InlineData(NotificationStatus.Failed)]
    public void An_ended_notification_cannot_start_an_attempt(NotificationStatus end)
    {
        var notification = Queued();
        notification.StartAttempt();
        End(notification, end);

        Should.Throw<InvalidOperationException>(notification.StartAttempt);
        notification.Status.ShouldBe(end);
        notification.AttemptCount.ShouldBe(1);
    }

    [Theory]
    [InlineData(nameof(Domains.Notifications.Notification.Deliver))]
    [InlineData(nameof(Domains.Notifications.Notification.WaitForRetry))]
    [InlineData(nameof(Domains.Notifications.Notification.Fail))]
    public void Only_a_running_attempt_can_end(string end)
    {
        var queued = Queued();
        var waiting = Queued();
        waiting.StartAttempt();
        waiting.WaitForRetry();
        var delivered = Queued();
        delivered.StartAttempt();
        delivered.Deliver();
        var failed = Queued();
        failed.StartAttempt();
        failed.Fail();

        foreach (var (notification, status) in new[]
                 {
                     (Received(), NotificationStatus.Received),
                     (queued, NotificationStatus.Queued),
                     (waiting, NotificationStatus.RetryWaiting),
                     (delivered, NotificationStatus.Delivered),
                     (failed, NotificationStatus.Failed)
                 })
        {
            Should.Throw<InvalidOperationException>(() => EndBy(notification, end));
            notification.Status.ShouldBe(status);
        }
    }

    [Fact]
    public void A_delivering_notification_cannot_be_skipped()
    {
        var notification = Queued();
        notification.StartAttempt();

        Should.Throw<InvalidOperationException>(() => notification.Skip(SkipReason.ChannelNotSupported))
            .Message.ShouldBe("A notification that is Delivering cannot change its status.");
        notification.Status.ShouldBe(NotificationStatus.Delivering);
    }

    private static void End(Domains.Notifications.Notification notification, NotificationStatus end)
    {
        switch (end)
        {
            case NotificationStatus.Delivered:
                notification.Deliver();
                break;
            case NotificationStatus.Failed:
                notification.Fail();
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(end), end, "not an end");
        }
    }

    private static void EndBy(Domains.Notifications.Notification notification, string end)
    {
        switch (end)
        {
            case nameof(Domains.Notifications.Notification.Deliver):
                notification.Deliver();
                break;
            case nameof(Domains.Notifications.Notification.WaitForRetry):
                notification.WaitForRetry();
                break;
            case nameof(Domains.Notifications.Notification.Fail):
                notification.Fail();
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(end), end, "not an end");
        }
    }

    private static readonly RenderedMessage Rendered = new("Your account is open", "Dear Jane", BodyFormat.Html);

    [Fact]
    public void A_resumed_notification_with_no_attempt_made_is_queued()
    {
        EmailRecipient.TryCreate("jane@example.com", out var to).ShouldBeTrue();
        var id = Guid.CreateVersion7();
        var acceptedAt = new DateTimeOffset(2026, 10, 5, 8, 0, 0, TimeSpan.Zero);

        var notification = Domains.Notifications.Notification.Resume(id, "account-opened", "email", "treasury-ops", acceptedAt, to, null, Rendered, 0);

        notification.NotificationId.ShouldBe(id);
        notification.Status.ShouldBe(NotificationStatus.Queued);
        notification.AttemptCount.ShouldBe(0);
        notification.Recipient.ShouldBe(to);
        notification.RenderedMessage.ShouldBe(Rendered);
        notification.AcceptedAt.ShouldBe(acceptedAt);
        notification.Parameters.ShouldBeEmpty();
    }

    [Fact]
    public void A_resumed_notification_with_attempts_made_waits_for_its_next_attempt_and_still_gets_at_most_3()
    {
        TeamsRecipient.TryCreate("ops-alerts", out var to).ShouldBeTrue();

        var notification = Domains.Notifications.Notification.Resume(Guid.CreateVersion7(), "staff-account-opened", "teams", "treasury-ops",
            DateTimeOffset.UnixEpoch, null, to, Rendered, 2);

        notification.Status.ShouldBe(NotificationStatus.RetryWaiting);
        notification.AttemptCount.ShouldBe(2);
        notification.TeamsRecipient.ShouldBe(to);
        notification.StartAttempt();
        notification.AttemptCount.ShouldBe(3);
        notification.WaitForRetry();
        Should.Throw<InvalidOperationException>(notification.StartAttempt);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(3)]
    public void A_notification_cannot_resume_outside_its_attempts(int attemptsMade)
    {
        EmailRecipient.TryCreate("jane@example.com", out var to).ShouldBeTrue();
        Should.Throw<ArgumentOutOfRangeException>(() => Domains.Notifications.Notification.Resume(Guid.CreateVersion7(), "t", "email", "c",
            DateTimeOffset.UnixEpoch, to, null, Rendered, attemptsMade));
    }

    [Fact]
    public void A_notification_resumes_with_exactly_one_recipient()
    {
        EmailRecipient.TryCreate("jane@example.com", out var email).ShouldBeTrue();
        TeamsRecipient.TryCreate("ops-alerts", out var teams).ShouldBeTrue();
        Should.Throw<ArgumentException>(() => Domains.Notifications.Notification.Resume(Guid.CreateVersion7(), "t", "email", "c",
            DateTimeOffset.UnixEpoch, null, null, Rendered, 0));
        Should.Throw<ArgumentException>(() => Domains.Notifications.Notification.Resume(Guid.CreateVersion7(), "t", "email", "c",
            DateTimeOffset.UnixEpoch, email, teams, Rendered, 0));
    }
}
