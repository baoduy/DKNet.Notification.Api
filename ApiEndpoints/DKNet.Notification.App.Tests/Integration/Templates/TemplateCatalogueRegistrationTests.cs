using DKNet.Notification.App.TestSupport;
using DKNet.Notification.AppServices.Templates;

namespace DKNet.Notification.App.Tests.Integration.Templates;

/// <summary>
/// DRK-2017: the host registers the catalogue it loaded at start-up as one instance (DRK-2013 §5, R6), holding the
/// released template.
/// </summary>
public sealed class TemplateCatalogueRegistrationTests
{
    [Fact]
    public void TheHostServesTheReleasedCatalogueAsOneInstance()
    {
        using var factory = new ReleasedCatalogueApiFactory();

        var catalogue = factory.Services.GetRequiredService<ITemplateCatalogue>();

        factory.Services.GetRequiredService<ITemplateCatalogue>().ShouldBeSameAs(catalogue);
        var template = catalogue.Find("account-opened").ShouldNotBeNull();
        template.Versions.ShouldHaveSingleItem().Subject.ShouldBe("Your account is open");
    }

    private sealed class ReleasedCatalogueApiFactory : TestApiFactoryBase;
}
