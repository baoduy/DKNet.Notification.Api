using Microsoft.AspNetCore.Hosting;

namespace DKNet.Notification.App.BDDTests.Support;

public sealed class BddApiFactory : TestApiFactoryBase
{
    protected override void AddFeatureOverrides(IDictionary<string, string?> settings) =>
        settings["FeatureManagement:RequireAuthorization"] = "false";

    // Routes that exist only in this test host, for the ErrorHandling scenarios — see TestOnlyRoutes.
    protected override void ConfigureTestServices(IServiceCollection services) =>
        services.AddTransient<IStartupFilter, TestOnlyRoutes>();
}
