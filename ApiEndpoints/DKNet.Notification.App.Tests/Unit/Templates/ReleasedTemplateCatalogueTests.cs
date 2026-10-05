using DKNet.Notification.AppServices.Templates;
using DKNet.Notification.Domains.Templates;
using Microsoft.Extensions.Configuration;

namespace DKNet.Notification.App.Tests.Unit.Templates;

/// <summary>
/// DRK-2013 §5 <c>@unit</c> scenario "The release ships the sample template". It reads the released settings and
/// template folder from this test's own output folder, the copy the build makes, so it also fails when the
/// template files are not copied to the build output. Every expected value is a literal from the spec.
/// </summary>
public sealed class ReleasedTemplateCatalogueTests
{
    [Fact]
    public void TheReleaseShipsTheSampleTemplate()
    {
        var settings = new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: false)
            .Build();
        var registrations = settings.GetSection("Notifications:Templates").Get<List<TemplateRegistration>>() ?? [];

        var catalogue = TemplateCatalogueLoader.Load(registrations, Path.Combine(AppContext.BaseDirectory, "Templates"));

        catalogue.Templates.Select(t => t.TemplateId).ShouldBe(["account-opened"]);
        var template = catalogue.Find("account-opened");
        template.ShouldNotBeNull();
        var version = template.Versions.ShouldHaveSingleItem();
        version.Channel.ShouldBe("email");
        version.Format.ShouldBe(TemplateFormat.Html);
        version.Subject.ShouldBe("Your account is open");
        version.Body.ShouldStartWith("<!DOCTYPE html>");
        version.Body.ShouldContain(">Dear {{customerName}}, your account {{accountNumber}} is open.</p>");
    }
}
