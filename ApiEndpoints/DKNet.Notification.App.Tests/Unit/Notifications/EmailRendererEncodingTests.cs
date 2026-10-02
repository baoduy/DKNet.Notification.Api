using DKNet.Notification.AppServices.Notifications;
using DKNet.Notification.Domains.Notifications;
using DKNet.Notification.Domains.Templates;

namespace DKNet.Notification.App.Tests.Unit.Notifications;

/// <summary>
/// DRK-2020 §3 Step 8 rules no surface A acceptance test pins (brief DRK-2024 row 16): body values are HTML-encoded,
/// subject values are not, every CR and LF of the subject becomes a space, and the first of 2 parameter names that
/// differ only in case fills the token.
/// </summary>
public sealed class EmailRendererEncodingTests
{
    private static TemplateVersion EmailVersion(string? subject, string body) =>
        new("email", "test.email.html", TemplateFormat.Html, subject, Title: null, body);

    private static RenderedMessage Render(TemplateVersion version, Dictionary<string, string> parameters) =>
        EmailRenderer.Render(version, parameters).Message.ShouldNotBeNull();

    [Fact]
    public void A_body_value_is_HTML_encoded_and_a_subject_value_is_not()
    {
        var version = EmailVersion("Hello {{customerName}}", "<p>Dear {{customerName}}</p>");

        var message = Render(version, new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["customerName"] = "<b>Jane & \"Tan\"</b>"
        });

        message.Body.ShouldBe("<p>Dear &lt;b&gt;Jane &amp; &quot;Tan&quot;&lt;/b&gt;</p>");
        message.Subject.ShouldBe("Hello <b>Jane & \"Tan\"</b>");
    }

    [Theory]
    [InlineData("Jane\rTan", "Hello Jane Tan")]
    [InlineData("Jane\nTan", "Hello Jane Tan")]
    [InlineData("Jane\r\nBcc: eve@example.com", "Hello Jane  Bcc: eve@example.com")]
    public void Every_CR_and_LF_in_the_subject_becomes_a_space(string customerName, string subject)
    {
        var version = EmailVersion("Hello {{customerName}}", "Dear {{customerName}}");

        var message = Render(version, new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["customerName"] = customerName
        });

        message.Subject.ShouldBe(subject);
        message.Body.ShouldBe($"Dear {customerName}");
    }

    [Fact]
    public void A_subject_of_998_characters_is_kept_whole()
    {
        var customerName = string.Concat(Enumerable.Repeat("0123456789", 100))[..992];
        var version = EmailVersion("Hello {{customerName}}", "Account alert.");

        var message = Render(version, new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["customerName"] = customerName
        });

        message.Subject.ShouldBe("Hello " + customerName);
        message.Subject.Length.ShouldBe(998);
    }

    [Fact]
    public void The_first_of_2_names_that_differ_only_in_case_fills_the_token()
    {
        var version = EmailVersion("Hello {{customerName}}", "Dear {{CUSTOMERNAME}}");

        var message = Render(version, new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["CustomerName"] = "Jane",
            ["customername"] = "John"
        });

        message.Subject.ShouldBe("Hello Jane");
        message.Body.ShouldBe("Dear Jane");
    }

    [Fact]
    public void A_missing_body_token_is_named_as_the_template_writes_it()
    {
        var version = EmailVersion("Your account is open", "Dear {{customerName}}, account {{AccountNumber}}.");

        var rendering = EmailRenderer.Render(version, new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["customerName"] = "Jane"
        });

        rendering.MissingParameter.ShouldBe("AccountNumber");
        rendering.Message.ShouldBeNull();
    }

    [Fact]
    public void A_version_with_no_subject_renders_an_empty_subject()
    {
        var message = Render(EmailVersion(subject: null, "Dear Jane"), new Dictionary<string, string>(StringComparer.Ordinal));

        message.Subject.ShouldBe(string.Empty);
        message.Body.ShouldBe("Dear Jane");
        message.Format.ShouldBe(BodyFormat.Html);
    }
}
