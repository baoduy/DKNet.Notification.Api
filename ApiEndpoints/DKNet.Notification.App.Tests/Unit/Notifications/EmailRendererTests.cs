using DKNet.Notification.AppServices.Notifications;
using DKNet.Notification.Domains.Notifications;
using DKNet.Notification.Domains.Templates;

namespace DKNet.Notification.App.Tests.Unit.Notifications;

/// <summary>
/// DRK-2020 §5, rule "The template is filled from the parameters": the @unit scenarios, one test each, named after
/// the scenario. Every expected value is a literal from the spec. The templates are built here from the spec text
/// (§2: the body "Dear {{customerName}}, your account {{accountNumber}} is open."), not read from the release.
/// </summary>
public sealed class EmailRendererTests
{
    private static TemplateVersion EmailVersion(string subject, string body) =>
        new("email", "test.email.html", TemplateFormat.Html, subject, Title: null, body);

    private static RenderedMessage Rendered(EmailRendering rendering)
    {
        rendering.MissingParameter.ShouldBeNull();
        var message = rendering.Message.ShouldNotBeNull();
        message.Format.ShouldBe(BodyFormat.Html);
        return message;
    }

    [Theory(DisplayName = "Only double braces are tokens, and a value is never read as a token")]
    [InlineData("Dear {{customerName}}", "Jane", "Dear Jane")]
    [InlineData("Dear {{CustomerName}}", "Jane", "Dear Jane")]
    [InlineData("Dear [customerName]", "Jane", "Dear [customerName]")]
    [InlineData("Dear {customerName}", "Jane", "Dear {customerName}")]
    [InlineData("Dear <customerName>", "Jane", "Dear <customerName>")]
    [InlineData("Dear {{customerName}}", "{{accountNumber}}", "Dear {{accountNumber}}")]
    public void Only_double_braces_are_tokens_and_a_value_is_never_read_as_a_token(
        string body,
        string customer,
        string result)
    {
        // Template "greeting"; the spec gives no subject, so this one holds no token.
        var greeting = EmailVersion("Greeting", body);
        var parameters = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["customerName"] = customer,
            // Present so the last row can only stay literal by never being read as a token.
            ["accountNumber"] = "0012345678"
        };

        var message = Rendered(EmailRenderer.Render(greeting, parameters));

        message.Body.ShouldBe(result);
    }

    [Fact(DisplayName = "An empty value and an unused parameter are accepted")]
    public void An_empty_value_and_an_unused_parameter_are_accepted()
    {
        var accountOpened = EmailVersion(
            "Your account is open",
            "Dear {{customerName}}, your account {{accountNumber}} is open.");
        var parameters = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["customerName"] = "",
            ["accountNumber"] = "0012345678",
            ["branch"] = "Tampines"
        };

        var message = Rendered(EmailRenderer.Render(accountOpened, parameters));

        message.Body.ShouldBe("Dear , your account 0012345678 is open.");
        message.Subject.ShouldBe("Your account is open");
    }

    [Fact(DisplayName = "A long subject is cut at 998 characters")]
    public void A_long_subject_is_cut_at_998_characters()
    {
        var accountAlert = EmailVersion("Alert for {{customerName}}", "Account alert.");
        // 4,000 characters that are not all the same, so a cut at the wrong place or from the wrong end shows.
        var customerName = string.Concat(Enumerable.Repeat("0123456789", 400));
        customerName.Length.ShouldBe(4000);
        var filledSubject = "Alert for " + customerName;

        var message = Rendered(EmailRenderer.Render(
            accountAlert,
            new Dictionary<string, string>(StringComparer.Ordinal) { ["customerName"] = customerName }));

        message.Subject.ShouldBe(filledSubject[..998]);
    }

    [Fact(DisplayName = "The subject's tokens are checked before the body's")]
    public void The_subjects_tokens_are_checked_before_the_bodys()
    {
        var accountAlert = EmailVersion("Alert for {{customerName}}", "Account {{accountNumber}}");

        var rendering = EmailRenderer.Render(accountAlert, new Dictionary<string, string>(StringComparer.Ordinal));

        rendering.MissingParameter.ShouldBe("customerName");
        rendering.Message.ShouldBeNull();
    }
}
