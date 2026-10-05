using System.Text.RegularExpressions;
using DKNet.Notification.AppServices.Templates;
using DKNet.Notification.Domains.Templates;

namespace DKNet.Notification.App.Tests.Unit.Templates;

/// <summary>
/// DRK-2017 programmer tests for <see cref="TemplateCatalogueLoader" />: the edge cases the frozen outline leaves
/// open (DRK-2018 LEFT OPEN) and the limits of each rule in DRK-2013 §6. Each test owns a temp template folder.
/// </summary>
public sealed class TemplateCatalogueLoaderTests : IDisposable
{
    private const string EmailFile = "account-opened.email.html";
    private const string TeamsFile = "account-opened.teams.md";
    private const string EmailBody = "<title>Your account is open</title><p>Dear {{customerName}}.</p>";
    private const string TeamsBody = "**Dear {{customerName}}.**";
    private const string IdRule = "R1 the id must match ^[a-z0-9-]{1,100}$.";

    private readonly string _root = Path.Combine(Path.GetTempPath(), $"drk-2017-templates-{Guid.NewGuid():N}");
    private readonly string _templateFolder;

    public TemplateCatalogueLoaderTests()
    {
        _templateFolder = Path.Combine(_root, "Templates");
        Directory.CreateDirectory(_templateFolder);
        File.WriteAllText(Path.Combine(_templateFolder, EmailFile), EmailBody);
        File.WriteAllText(Path.Combine(_templateFolder, TeamsFile), TeamsBody);
    }

    [Fact]
    public void ARootedFilePathIsRefused()
    {
        // The file exists, so only its rooted path breaks the rule.
        var template = Template("account-opened", Email(Path.Combine(_templateFolder, EmailFile)));

        ShouldBreak(template, "^Template 'account-opened': R4 the file must sit inside the template folder\\.$");
    }

    [Fact]
    public void AFileInASiblingFolderWithTheSamePrefixIsRefused()
    {
        var sibling = _templateFolder + "-other";
        Directory.CreateDirectory(sibling);
        File.WriteAllText(Path.Combine(sibling, EmailFile), EmailBody);

        ShouldBreak(Template("account-opened", Email($"../Templates-other/{EmailFile}")),
            "^Template 'account-opened': R4 the file must sit inside the template folder\\.$");
    }

    [Fact]
    public void AFileThatCannotBeReadIsRefused()
    {
        using var locked = new FileStream(
            Path.Combine(_templateFolder, EmailFile), FileMode.Open, FileAccess.ReadWrite, FileShare.None);

        var error = Should.Throw<InvalidOperationException>(
            () => TemplateCatalogueLoader.Load([Template("account-opened", Email(EmailFile))], _templateFolder));

        error.Message.ShouldBe($"Template 'account-opened': R4 the file '{EmailFile}' cannot be read.");
        error.InnerException.ShouldBeAssignableTo<IOException>();
    }

    [Fact]
    public void AFileWithNoReadPermissionIsRefused()
    {
        // Unix file modes only. The suite runs on Linux and macOS, as a user that file modes bind.
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        var path = Path.Combine(_templateFolder, EmailFile);
        File.SetUnixFileMode(path, UnixFileMode.None);

        var error = Should.Throw<InvalidOperationException>(
            () => TemplateCatalogueLoader.Load([Template("account-opened", Email(EmailFile))], _templateFolder));

        error.Message.ShouldBe($"Template 'account-opened': R4 the file '{EmailFile}' cannot be read.");
        error.InnerException.ShouldBeOfType<UnauthorizedAccessException>();
    }

    [Fact]
    public void AFileNameOf201CharactersIsNamed()
    {
        var longName = new string('f', 196) + ".html";
        File.WriteAllText(Path.Combine(_templateFolder, longName), EmailBody);

        ShouldBreak(Template("account-opened", Email(longName)),
            "^Template 'account-opened': R4 the file name is longer than 200 characters\\.$");
    }

    [Fact]
    public void AFileThatDoesNotExistIsNamed() =>
        ShouldBreak(Template("account-opened", Email("missing.html")),
            "^Template 'account-opened': R4 the file 'missing\\.html' does not exist\\.$");

    [Fact]
    public void ATitleOf201CharactersIsRefused()
    {
        Write(TeamsFile, "title: " + new string('t', 201), TeamsBody);

        ShouldBreak(Template("account-opened", Teams(TeamsFile)),
            "^Template 'account-opened': R5 the title is longer than 200 characters\\.$");
    }

    [Fact]
    public void ASecondTemplateWithTheSameIdIsNamed() =>
        Should.Throw<InvalidOperationException>(() => TemplateCatalogueLoader.Load(
                [Template("account-opened", Email(EmailFile)), Template("account-opened", Email(EmailFile))],
                _templateFolder))
            .Message.ShouldBe("Template 'account-opened': R1 another template has the same id.");

    [Fact]
    public void ADescriptionOf201CharactersIsNamed()
    {
        var template = Template("account-opened", Email(EmailFile));
        template.Description = new string('d', 201);

        ShouldBreak(template, "^Template 'account-opened': R5 the description is longer than 200 characters\\.$");
    }

    [Fact]
    public void AnIdOf101CharactersIsRefused()
    {
        var id = new string('a', 101);

        ShouldBreakWith(Template(id, Email(EmailFile)), $"Template '{id}': {IdRule}");
    }

    [Theory]
    [InlineData("")]
    [InlineData("account-opened\n")]
    [InlineData("account opened")]
    public void AnIdOutsideThePatternIsRefused(string id) =>
        ShouldBreakWith(Template(id, Email(EmailFile)), $"Template '{id}': {IdRule}");

    [Fact]
    public void AnIdTheConfigurationLeftNullIsRefused() =>
        ShouldBreakWith(new TemplateRegistration { TemplateId = null!, Versions = { Email(EmailFile) } },
            $"Template '': {IdRule}");

    [Theory]
    [InlineData("Email")]
    [InlineData("Teams")]
    [InlineData("")]
    [InlineData(null)]
    public void AChannelOtherThanExactlyEmailOrTeamsIsRefused(string? channel)
    {
        var version = Email(EmailFile);
        version.Channel = channel!;

        ShouldBreak(Template("account-opened", version),
            "^Template 'account-opened': R2 the channel must be email or teams\\.$");
    }

    [Fact]
    public void ADuplicateTeamsVersionIsRefused() =>
        ShouldBreak(Template("account-opened", Teams(TeamsFile), Teams(TeamsFile)),
            "^Template 'account-opened': R2 the template has more than 1 teams version\\.$");

    [Fact]
    public void AVersionsListTheConfigurationLeftNullIsRefused() =>
        ShouldBreak(new TemplateRegistration { TemplateId = "account-opened", Versions = null! },
            "^Template 'account-opened': R2 the template has no version\\.$");

    [Fact]
    public void AVersionWithNoFormatIsRefused()
    {
        var version = Email(EmailFile);
        version.Format = null;

        ShouldBreak(Template("account-opened", version),
            "^Template 'account-opened': R3 the email version must be in Html\\.$");
    }

    [Fact]
    public void ATeamsVersionInHtmlNamesMarkdown()
    {
        var version = Teams(TeamsFile);
        version.Format = TemplateFormat.Html;

        ShouldBreak(Template("account-opened", version),
            "^Template 'account-opened': R3 the teams version must be in Markdown\\.$");
    }

    [Fact]
    public void AnEmailVersionWithAnEmptySubjectIsRefused()
    {
        File.WriteAllText(Path.Combine(_templateFolder, EmailFile), "<title> </title><p>Dear {{customerName}}.</p>");

        ShouldBreak(Template("account-opened", Email(EmailFile)),
            "^Template 'account-opened': R3 an email version needs a <title> of 1 to 500 characters\\.$");
    }

    [Fact]
    public void TheEmailSubjectIsTheDecodedTitleAndTheTitleStaysInTheBody()
    {
        const string body = "<head>\n<TITLE lang=\"en\">\n  Tom &amp; {{customerName}}\n</TITLE>\n</head><p>Hi.</p>";
        File.WriteAllText(Path.Combine(_templateFolder, EmailFile), body);

        var version = TemplateCatalogueLoader.Load([Template("account-opened", Email(EmailFile))], _templateFolder)
            .Find("account-opened")!.Versions.ShouldHaveSingleItem();

        version.Subject.ShouldBe("Tom & {{customerName}}");
        version.Body.ShouldBe(body);
    }

    [Fact]
    public void ATeamsVersionWithNoFrontMatterLoadsWithNoTitle() =>
        TemplateCatalogueLoader.Load([Template("account-opened", Teams(TeamsFile))], _templateFolder)
            .Find("account-opened")!.Versions.ShouldHaveSingleItem().Title.ShouldBeNull();

    [Fact]
    public void AFrontMatterWithWindowsLineEndingsAndQuotedValuesLoads()
    {
        File.WriteAllText(Path.Combine(_templateFolder, TeamsFile),
            "---\r\ntitle: \"Account {{accountNumber}} opened\"\r\nauthor: 'ops'\r\n---\r\n" + TeamsBody);

        var version = TemplateCatalogueLoader.Load([Template("account-opened", Teams(TeamsFile))], _templateFolder)
            .Find("account-opened")!.Versions.ShouldHaveSingleItem();

        version.Title.ShouldBe("Account {{accountNumber}} opened");
        version.Body.ShouldBe(TeamsBody);
    }

    [Fact]
    public void AFrontMatterThatIsNeverClosedIsRefused()
    {
        File.WriteAllText(Path.Combine(_templateFolder, TeamsFile), "---\ntitle: Account opened\n" + TeamsBody);

        ShouldBreak(Template("account-opened", Teams(TeamsFile)),
            $"^Template 'account-opened': R4 the file '{Regex.Escape(TeamsFile)}' opens a front matter it never closes\\.$");
    }

    [Fact]
    public void AFrontMatterLineThatIsNotKeyValueIsRefused()
    {
        Write(TeamsFile, "Account opened", TeamsBody);

        ShouldBreak(Template("account-opened", Teams(TeamsFile)),
            $"^Template 'account-opened': R4 the file '{Regex.Escape(TeamsFile)}' has a front matter line that is not 'key: value'\\.$");
    }

    [Fact]
    public void AFileNameTheConfigurationLeftNullIsRefused()
    {
        var version = Email(EmailFile);
        version.File = null!;

        ShouldBreak(Template("account-opened", version),
            "^Template 'account-opened': R4 the file '' does not exist\\.$");
    }

    [Fact]
    public void EveryValueAtItsLimitLoads()
    {
        var id = new string('a', 100);
        var longName = new string('f', 195) + ".html";
        longName.Length.ShouldBe(200);
        File.WriteAllText(Path.Combine(_templateFolder, longName), $"<title>{new string('s', 500)}</title>");
        Write(TeamsFile, "title: " + new string('t', 200), TeamsBody);
        var template = Template(id, Email(longName), Teams(TeamsFile));
        template.Description = new string('d', 200);

        var catalogue = TemplateCatalogueLoader.Load([template], _templateFolder);

        var loaded = catalogue.Find(id).ShouldNotBeNull();
        loaded.Description.Length.ShouldBe(200);
        loaded.Versions.Select(v => v.Channel).ShouldBe(["email", "teams"]);
        loaded.Versions[0].Subject!.Length.ShouldBe(500);
        loaded.Versions[1].Title!.Length.ShouldBe(200);
    }

    [Fact]
    public void TheCatalogueHoldsEveryVersionWithItsFileText()
    {
        Write(TeamsFile, "title: Account opened", TeamsBody);
        var registrations = new[]
        {
            Template("account-opened", Email(EmailFile), Teams(TeamsFile)),
            Template("account-closed", Email(EmailFile))
        };

        // A trailing separator on the folder does not move it.
        var catalogue = TemplateCatalogueLoader.Load(registrations, _templateFolder + Path.DirectorySeparatorChar);

        catalogue.Templates.Select(t => t.TemplateId).ShouldBe(["account-opened", "account-closed"]);
        var template = catalogue.Find("account-opened").ShouldNotBeNull();
        template.Description.ShouldBe("A note for authors.");
        template.Versions.ShouldBe(
        [
            new TemplateVersion("email", EmailFile, TemplateFormat.Html, "Your account is open", null, EmailBody),
            new TemplateVersion("teams", TeamsFile, TemplateFormat.Markdown, null, "Account opened", TeamsBody)
        ]);
    }

    [Fact]
    public void ADescriptionTheConfigurationLeftNullLoadsAsEmpty()
    {
        var template = Template("account-opened", Email(EmailFile));
        template.Description = null!;

        TemplateCatalogueLoader.Load([template], _templateFolder).Find("account-opened")!.Description
            .ShouldBe(string.Empty);
    }

    [Fact]
    public void FindMatchesTheIdExactly()
    {
        var catalogue = TemplateCatalogueLoader.Load([Template("account-opened", Email(EmailFile))], _templateFolder);

        catalogue.Find("account-opened").ShouldNotBeNull();
        catalogue.Find("Account-Opened").ShouldBeNull();
        catalogue.Find("account-opened ").ShouldBeNull();
    }

    [Fact]
    public void AnEmptyCatalogueLoads() =>
        TemplateCatalogueLoader.Load([], _templateFolder).Templates.ShouldBeEmpty();

    [Fact]
    public void LoadRefusesMissingArguments()
    {
        Should.Throw<ArgumentNullException>(() => TemplateCatalogueLoader.Load(null!, _templateFolder));
        Should.Throw<ArgumentException>(() => TemplateCatalogueLoader.Load([], " "));
    }

    private void Write(string name, string frontMatter, string body) =>
        File.WriteAllText(Path.Combine(_templateFolder, name), $"---\n{frontMatter}\n---\n{body}");

    private void ShouldBreak(TemplateRegistration template, string messagePattern)
    {
        var error = Should.Throw<InvalidOperationException>(
            () => TemplateCatalogueLoader.Load([template], _templateFolder));

        error.Message.ShouldMatch(messagePattern);
    }

    private void ShouldBreakWith(TemplateRegistration template, string message) =>
        Should.Throw<InvalidOperationException>(() => TemplateCatalogueLoader.Load([template], _templateFolder))
            .Message.ShouldBe(message);

    private static TemplateRegistration Template(string id, params TemplateVersionRegistration[] versions)
    {
        var template = new TemplateRegistration { TemplateId = id, Description = "A note for authors." };
        foreach (var version in versions)
        {
            template.Versions.Add(version);
        }

        return template;
    }

    private static TemplateVersionRegistration Email(string file) => new()
    {
        Channel = "email",
        File = file,
        Format = TemplateFormat.Html
    };

    private static TemplateVersionRegistration Teams(string file) => new()
    {
        Channel = "teams",
        File = file,
        Format = TemplateFormat.Markdown
    };

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }
}
