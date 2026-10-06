using DKNet.Notification.AppServices.Delivery;
using DKNet.Notification.Domains.Notifications;

namespace DKNet.Notification.App.Tests.Unit.Delivery;

/// <summary>The queued message is personal data: the text a bus error or a log entry makes of it holds none.</summary>
public sealed class DeliverNotificationTests
{
    [Fact]
    public void The_text_of_a_message_names_its_ids_and_attempts_but_not_its_recipient_subject_or_body()
    {
        var id = Guid.CreateVersion7();
        var email = new DeliverNotification(
            DeliverNotification.CurrentSchemaVersion, id, "account-opened", "email", "treasury-ops", "order-42",
            DateTimeOffset.UnixEpoch, "trace-1", "jane@example.com", null, "Your account is open", "Dear Jane Tan, welcome.",
            BodyFormat.Html, 1, DateTimeOffset.UnixEpoch);
        var teams = email with { Channel = "teams", EmailAddress = null, TeamsDestination = "ops-alerts" };

        var text = email.ToString();

        text.ShouldContain(id.ToString());
        text.ShouldContain("AttemptsMade = 1");
        text.ShouldContain("trace-1");
        foreach (var message in new[] { email, teams })
        {
            message.ToString().ShouldNotContain("jane@example.com");
            message.ToString().ShouldNotContain("ops-alerts");
            message.ToString().ShouldNotContain("Your account is open");
            message.ToString().ShouldNotContain("Dear Jane Tan");
        }
    }
}
