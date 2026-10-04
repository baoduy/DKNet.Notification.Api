using DKNet.Notification.AppServices.Notifications;
using DKNet.Notification.Domains.Notifications;
using DKNet.Notification.Domains.Templates;

namespace DKNet.Notification.App.Tests.Unit.Notifications;

/// <summary>
///     DRK-2035 §3 "Rendering and the card" (brief DRK-2036 §6a D5): the title, then the Markdown body, filled with
///     the values unchanged; a title over 500 characters is cut; no title gives an empty one.
/// </summary>
public sealed class TeamsRendererTests
{
    private static TemplateVersion Teams(string? title, string body) =>
        new("teams", "staff-note.teams.md", TemplateFormat.Markdown, Subject: null, title, body);

    private static EmailRendering Render(TemplateVersion version, params (string Key, string Value)[] parameters) =>
        TeamsRenderer.Render(version, parameters.ToDictionary(p => p.Key, p => p.Value, StringComparer.Ordinal));

    [Fact]
    public void The_title_and_the_body_are_filled_as_Markdown()
    {
        var rendering = Render(
            Teams("Account {{accountNumber}} opened", "**{{customerName}}** opened account {{accountNumber}}."),
            ("customerName", "Jane Tan"),
            ("accountNumber", "0012345678"));

        rendering.MissingParameter.ShouldBeNull();
        rendering.Message.ShouldBe(new RenderedMessage(
            "Account 0012345678 opened",
            "**Jane Tan** opened account 0012345678.",
            BodyFormat.Markdown));
    }

    [Fact]
    public void A_value_goes_in_unchanged_with_no_encoding_and_no_line_break_change()
    {
        const string value = "\"}]<b>&[Jane](https://example.com)\r\n**x**";

        var rendering = Render(Teams("{{v}}", "{{v}}"), ("v", value));

        rendering.Message.ShouldBe(new RenderedMessage(value, value, BodyFormat.Markdown));
    }

    [Fact]
    public void A_token_is_matched_without_case_and_a_filled_value_is_never_read_as_a_token()
    {
        var rendering = Render(Teams(null, "{{CustomerName}} {{other}}"), ("customerName", "{{other}}"), ("other", "x"));

        rendering.Message.ShouldNotBeNull().Body.ShouldBe("{{other}} x");
    }

    [Fact]
    public void A_version_with_no_title_gives_an_empty_title() =>
        Render(Teams(null, "Note for {{team}}."), ("team", "Treasury")).Message
            .ShouldBe(new RenderedMessage(string.Empty, "Note for Treasury.", BodyFormat.Markdown));

    [Fact]
    public void A_title_that_is_empty_after_filling_stays_empty() =>
        Render(Teams("{{headline}}", "Note"), ("headline", string.Empty)).Message.ShouldNotBeNull().Subject.ShouldBe(string.Empty);

    [Fact]
    public void A_title_of_500_characters_is_kept_whole()
    {
        var headline = new string('h', 500);

        Render(Teams("{{headline}}", "Note"), ("headline", headline)).Message.ShouldNotBeNull().Subject.ShouldBe(headline);
    }

    [Fact]
    public void A_title_of_501_characters_is_cut_to_its_first_500()
    {
        var headline = new string('h', 500) + "X";

        Render(Teams("{{headline}}", "Note"), ("headline", headline)).Message.ShouldNotBeNull()
            .Subject.ShouldBe(new string('h', 500));
    }

    [Fact]
    public void The_body_is_never_cut()
    {
        var body = new string('b', 30_000);

        Render(Teams(null, "{{body}}"), ("body", body)).Message.ShouldNotBeNull().Body.ShouldBe(body);
    }

    [Fact]
    public void A_missing_token_in_the_title_is_named_before_one_in_the_body()
    {
        var rendering = Render(Teams("{{headline}}", "{{team}}"));

        rendering.Message.ShouldBeNull();
        rendering.MissingParameter.ShouldBe("headline");
    }

    [Fact]
    public void A_missing_token_in_the_body_is_named()
    {
        var rendering = Render(Teams("Note", "{{team}} and {{other}}"), ("other", "x"));

        rendering.Message.ShouldBeNull();
        rendering.MissingParameter.ShouldBe("team");
    }
}
