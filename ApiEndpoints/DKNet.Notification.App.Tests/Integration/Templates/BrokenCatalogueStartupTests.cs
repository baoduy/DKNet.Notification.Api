using DKNet.Notification.App.TestSupport;
using Microsoft.AspNetCore.Hosting;

namespace DKNet.Notification.App.Tests.Integration.Templates;

/// <summary>
/// DRK-2013 §5 outline "A broken template catalogue stops the start-up", row "the template id
/// \"Account_Opened\"", through the real host start (brief Q2: every row at the loader, at least 1 through the
/// host). The registration is otherwise valid and names the released file, so only its id breaks a rule.
/// </summary>
/// <remarks>
/// The settings go in through <c>UseSetting</c>: the host reads <c>Notifications:Templates</c> before
/// <c>Build()</c>, which a <c>ConfigureAppConfiguration</c> override reaches too late. Index 90 keeps the entry
/// clear of the released registrations at the start of the list.
/// </remarks>
public sealed class BrokenCatalogueStartupTests
{
    [Fact]
    public void ABrokenTemplateCatalogueStopsTheStartUp_ThroughTheHost()
    {
        using var factory = new BrokenCatalogueApiFactory();

        var error = Should.Throw<Exception>(() => factory.Services);

        var refusal = Chain(error).OfType<InvalidOperationException>()
            .FirstOrDefault(e => e.Message.StartsWith("Template '", StringComparison.Ordinal));
        refusal.ShouldNotBeNull($"the start-up must fail on the catalogue, but failed with: {error}");
        refusal.Message.ShouldMatch(@"^Template 'Account_Opened': R1\b");
    }

    private static IEnumerable<Exception> Chain(Exception error)
    {
        for (var current = error; current is not null; current = current.InnerException)
        {
            yield return current;
        }
    }

    private sealed class BrokenCatalogueApiFactory : TestApiFactoryBase
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.UseSetting("Notifications:Templates:90:TemplateId", "Account_Opened");
            builder.UseSetting("Notifications:Templates:90:Versions:0:Channel", "email");
            builder.UseSetting("Notifications:Templates:90:Versions:0:File", "account-opened.email.html");
            builder.UseSetting("Notifications:Templates:90:Versions:0:Format", "Html");
        }
    }
}
