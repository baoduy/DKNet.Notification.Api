using System.Text;
using System.Text.Json;
using DKNet.Notification.AppServices.Delivery;
using DKNet.Notification.Domains.Notifications;

namespace DKNet.Notification.App.Tests.Unit.Delivery;

/// <summary>
///     DRK-2035 §3 "Rendering and the card" and brief DRK-2036 §5: the posted message, byte for byte. Every expected
///     message is the brief's literal, never one the card writer made.
/// </summary>
public sealed class TeamsCardTests
{
    private const string WithTitle =
        """{"type":"message","attachments":[{"contentType":"application/vnd.microsoft.card.adaptive","content":{"type":"AdaptiveCard","$schema":"http://adaptivecards.io/schemas/adaptive-card.json","version":"1.4","body":[{"type":"TextBlock","text":"Account 0012345678 opened","wrap":true,"weight":"Bolder","size":"Medium"},{"type":"TextBlock","text":"**Jane Tan** opened account 0012345678.","wrap":true}]}}]}""";

    private const string NoTitle =
        """{"type":"message","attachments":[{"contentType":"application/vnd.microsoft.card.adaptive","content":{"type":"AdaptiveCard","$schema":"http://adaptivecards.io/schemas/adaptive-card.json","version":"1.4","body":[{"type":"TextBlock","text":"Note for Treasury.","wrap":true}]}}]}""";

    private static string Text(byte[] bytes) => Encoding.UTF8.GetString(bytes);

    [Fact]
    public void A_message_with_a_title_is_a_title_block_then_the_Markdown_block() =>
        Text(TeamsCard.Serialize(new RenderedMessage(
                "Account 0012345678 opened",
                "**Jane Tan** opened account 0012345678.",
                BodyFormat.Markdown)))
            .ShouldBe(WithTitle);

    [Fact]
    public void A_message_with_an_empty_title_has_no_title_block() =>
        Text(TeamsCard.Serialize(new RenderedMessage(string.Empty, "Note for Treasury.", BodyFormat.Markdown)))
            .ShouldBe(NoTitle);

    [Fact]
    public void A_value_that_looks_like_JSON_stays_text_inside_its_block()
    {
        const string value = "\"}]},{\"type\":\"Image\"";

        var bytes = TeamsCard.Serialize(new RenderedMessage(string.Empty, value, BodyFormat.Markdown));

        Text(bytes).ShouldBe(NoTitle.Replace(
            "\"text\":\"Note for Treasury.\"",
            "\"text\":\"\\u0022}]},{\\u0022type\\u0022:\\u0022Image\\u0022\"",
            StringComparison.Ordinal));
        using var json = JsonDocument.Parse(bytes);
        var block = json.RootElement.GetProperty("attachments")[0].GetProperty("content").GetProperty("body")
            .EnumerateArray().ShouldHaveSingleItem();
        block.GetProperty("text").GetString().ShouldBe(value);
    }

    [Fact]
    public void The_largest_message_is_28672_bytes() => TeamsCard.MaxBytes.ShouldBe(28_672);

    [Fact]
    public void A_missing_message_is_refused() =>
        Should.Throw<ArgumentNullException>(() => TeamsCard.Serialize(null!)).ParamName.ShouldBe("message");
}
