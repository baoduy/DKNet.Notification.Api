using DKNet.Notification.AppServices.Templates;
using DKNet.Notification.Domains.Templates;

namespace DKNet.Notification.App.Tests.Unit.Templates;

/// <summary>
/// DRK-2013 §5 <c>@unit</c> outline "A broken template catalogue stops the start-up", every row at the loader
/// (brief Q2). Each test starts from a valid catalogue in its own temp folder, with every file on disk, and
/// breaks exactly one rule, so the rule the loader names cannot depend on its check order.
/// The message is the brief's Q1 default, <c>Template '&lt;id&gt;': &lt;rule&gt;</c>, with the rule named by
/// its id in the brief's §6 (R1 to R5).
/// </summary>
public sealed class BrokenTemplateCatalogueTests : IDisposable
{
    private const string TemplateId = "account-opened";
    private const string EmailFile = "account-opened.email.html";
    private const string EmailSubject = "Your account is open";

    private readonly string _root = Path.Combine(Path.GetTempPath(), $"drk-2013-templates-{Guid.NewGuid():N}");
    private readonly string _templateFolder;

    public BrokenTemplateCatalogueTests()
    {
        _templateFolder = Path.Combine(_root, "Templates");
        Directory.CreateDirectory(_templateFolder);
        WriteTemplateFile(EmailFile);
    }

    [Theory]
    [InlineData("the template id \"Account_Opened\"", "Account_Opened", "R1")]
    [InlineData("2 templates with the id \"account-opened\"", TemplateId, "R1")]
    [InlineData("the template \"account-opened\" with no version", TemplateId, "R2")]
    [InlineData("the template \"account-opened\" with 2 email versions", TemplateId, "R2")]
    [InlineData("an email version in Markdown", TemplateId, "R3")]
    [InlineData("an email version with no subject", TemplateId, "R3")]
    [InlineData("a Teams version in HTML", TemplateId, "R3")]
    [InlineData("a version for the channel \"sms\"", TemplateId, "R2")]
    [InlineData("a version whose file does not exist", TemplateId, "R4")]
    [InlineData("a version whose file sits outside the template folder", TemplateId, "R4")]
    [InlineData("an email version with a subject of 501 characters", TemplateId, "R3")]
    [InlineData("a template with a description of 201 characters", TemplateId, "R5")]
    [InlineData("a version whose file name is 201 characters long", TemplateId, "R4")]
    public void ABrokenTemplateCatalogueStopsTheStartUp(string fault, string namedTemplateId, string brokenRule)
    {
        var registrations = CatalogueHolding(fault);

        var error = Should.Throw<InvalidOperationException>(
            () => TemplateCatalogueLoader.Load(registrations, _templateFolder));

        error.Message.ShouldMatch($@"^Template '{namedTemplateId}': {brokenRule}\b");
    }

    private List<TemplateRegistration> CatalogueHolding(string fault)
    {
        var template = ValidTemplate();
        var registrations = new List<TemplateRegistration> { template };
        var email = template.Versions[0];

        switch (fault)
        {
            case "the template id \"Account_Opened\"":
                template.TemplateId = "Account_Opened";
                break;
            case "2 templates with the id \"account-opened\"":
                registrations.Add(ValidTemplate());
                break;
            case "the template \"account-opened\" with no version":
                template.Versions.Clear();
                break;
            case "the template \"account-opened\" with 2 email versions":
                template.Versions.Add(EmailVersion(EmailFile));
                break;
            case "an email version in Markdown":
                email.Format = TemplateFormat.Markdown;
                break;
            case "an email version with no subject":
                email.Subject = null;
                break;
            case "a Teams version in HTML":
                WriteTemplateFile("account-opened.teams.md");
                template.Versions.Add(new TemplateVersionRegistration
                {
                    Channel = "teams",
                    File = "account-opened.teams.md",
                    Format = TemplateFormat.Html
                });
                break;
            case "a version for the channel \"sms\"":
                email.Channel = "sms";
                break;
            case "a version whose file does not exist":
                email.File = "account-opened.missing.html";
                break;
            case "a version whose file sits outside the template folder":
                // The file exists, one level above the template folder: only its place breaks the rule.
                File.WriteAllText(Path.Combine(_root, "outside.html"), "<p>outside</p>");
                email.File = "../outside.html";
                break;
            case "an email version with a subject of 501 characters":
                email.Subject = new string('s', 501);
                break;
            case "a template with a description of 201 characters":
                template.Description = new string('d', 201);
                break;
            case "a version whose file name is 201 characters long":
                // The file exists, so only the length of its name breaks the rule.
                var longName = new string('f', 196) + ".html";
                longName.Length.ShouldBe(201);
                WriteTemplateFile(longName);
                email.File = longName;
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(fault), fault, "No such row in the outline.");
        }

        return registrations;
    }

    private static TemplateRegistration ValidTemplate() => new()
    {
        TemplateId = TemplateId,
        Description = "Sent when an account is opened.",
        Versions = { EmailVersion(EmailFile) }
    };

    private static TemplateVersionRegistration EmailVersion(string file) => new()
    {
        Channel = "email",
        File = file,
        Format = TemplateFormat.Html,
        Subject = EmailSubject
    };

    private void WriteTemplateFile(string name) =>
        File.WriteAllText(Path.Combine(_templateFolder, name), "<p>Dear {{customerName}}.</p>");

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }
}
