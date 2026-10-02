using DKNet.Notification.Domains.Notifications;

namespace DKNet.Notification.App.Tests.Unit.Notifications;

/// <summary>DRK-2020 §3 Step 6: exactly 1 address in <c>local@domain</c> form, at most 254 characters, never trimmed.</summary>
public sealed class EmailRecipientTests
{
    /// <summary>64 + 1 + 63 + 1 + 63 + 1 + 57 + 4 = 254 characters, each label inside its own limit.</summary>
    private static readonly string Address254 =
        $"{new string('a', 64)}@{new string('b', 63)}.{new string('c', 63)}.{new string('d', 57)}.com";

    [Theory]
    [InlineData("jane@example.com")]
    [InlineData("jane.tan+alerts@mail.example.com")]
    [InlineData("jane.tan@example.com")]
    public void A_bare_address_is_kept_exactly_as_sent(string value)
    {
        EmailRecipient.TryCreate(value, out var recipient).ShouldBeTrue();

        recipient.ShouldNotBeNull().Address.ShouldBe(value);
    }

    [Fact]
    public void An_address_of_254_characters_is_kept()
    {
        Address254.Length.ShouldBe(254);

        EmailRecipient.TryCreate(Address254, out var recipient).ShouldBeTrue();

        recipient.ShouldNotBeNull().Address.ShouldBe(Address254);
    }

    [Theory]
    [InlineData("")]
    [InlineData("Jane <jane@example.com>")]
    [InlineData("a@example.com,b@example.com")]
    [InlineData("jane.example.com")]
    [InlineData(" jane@example.com")]
    [InlineData("jane@example.com ")]
    [InlineData("jane@example.com (Jane)")]
    [InlineData("Jane<jane@example.com>")]
    [InlineData("jane@example.com(Jane)")]
    [InlineData("\"jane tan\"@example.com")]
    [InlineData("jane@example.com\r\nBcc: eve@example.com")]
    public void A_value_that_is_not_1_bare_address_is_refused(string value)
    {
        EmailRecipient.TryCreate(value, out var recipient).ShouldBeFalse();

        recipient.ShouldBeNull();
    }

    /// <summary>
    ///     DRK-2026 row 1: a mail header holds only an unquoted dot-atom local part. <c>MailAddress</c> already refuses
    ///     the leading and doubled dot; it takes the trailing dot and the quoted local part, which this rule refuses.
    /// </summary>
    [Theory]
    [InlineData("jane.@example.com")]
    [InlineData(".jane@example.com")]
    [InlineData("ja..ne@example.com")]
    [InlineData("\"jane\"@example.com")]
    public void A_local_part_that_is_not_an_unquoted_dot_atom_is_refused(string value)
    {
        EmailRecipient.TryCreate(value, out var recipient).ShouldBeFalse();

        recipient.ShouldBeNull();
    }

    [Fact]
    public void An_address_of_255_characters_is_refused()
    {
        // One more character in the last label, so only the length breaks the rule.
        var address255 = $"{new string('a', 64)}@{new string('b', 63)}.{new string('c', 63)}.{new string('d', 58)}.com";
        address255.Length.ShouldBe(255);

        EmailRecipient.TryCreate(address255, out var recipient).ShouldBeFalse();

        recipient.ShouldBeNull();
    }
}
