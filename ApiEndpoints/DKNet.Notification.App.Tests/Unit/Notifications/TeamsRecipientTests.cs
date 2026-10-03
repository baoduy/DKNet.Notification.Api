using DKNet.Notification.Domains.Notifications;

namespace DKNet.Notification.App.Tests.Unit.Notifications;

/// <summary>DRK-2035 §3 (brief DRK-2036 §6a D1): 1 to 64 lowercase letters, digits and '-', never trimmed or lower-cased.</summary>
public sealed class TeamsRecipientTests
{
    [Theory]
    [InlineData("a")]
    [InlineData("ops-alerts")]
    [InlineData("team-007")]
    [InlineData("-")]
    [InlineData("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")]
    public void A_name_that_keeps_the_rule_is_kept_exactly_as_sent(string value)
    {
        TeamsRecipient.TryCreate(value, out var recipient).ShouldBeTrue();

        recipient.ShouldNotBeNull().Name.ShouldBe(value);
    }

    [Theory]
    [InlineData("")]
    [InlineData("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")]
    [InlineData("Ops-Alerts")]
    [InlineData("ops alerts")]
    [InlineData(" ops-alerts")]
    [InlineData("ops-alerts ")]
    [InlineData("ops_alerts")]
    [InlineData("ops-alerts\n")]
    [InlineData("ops.alerts")]
    [InlineData("opś-alerts")]
    [InlineData("ops-alerts٣")]
    public void A_name_that_breaks_the_rule_is_refused(string value)
    {
        TeamsRecipient.TryCreate(value, out var recipient).ShouldBeFalse();

        recipient.ShouldBeNull();
    }

    [Fact]
    public void The_rule_allows_64_characters_and_not_65()
    {
        TeamsRecipient.MaxLength.ShouldBe(64);
        TeamsRecipient.TryCreate(new string('a', 64), out _).ShouldBeTrue();
        TeamsRecipient.TryCreate(new string('a', 65), out _).ShouldBeFalse();
    }
}
