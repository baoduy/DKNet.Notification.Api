using DKNet.EfCore.DataAuthorization;
using Microsoft.Extensions.DependencyInjection.Extensions;
using DKNet.Notification.App.TestSupport;

namespace DKNet.Notification.App.Tests.Integration.Support;

/// <summary>
/// <see cref="ApiFixture" /> variant with <c>FeatureManagement:RequireAuthorization</c> flipped on,
/// authenticated via <see cref="MultiSubjectAuthHandler" /> like <see cref="AuthOnMultiSubjectApiFixture" />,
/// but with <see cref="IDataOwnerProvider" /> replaced by <see cref="FixedTenantDataOwnerProvider" /> —
/// the tenant-ownership key stays <see cref="TenantKey" /> no matter which subject signs in, so a test can
/// prove the acting-user stamp (<c>CreatedBy</c>/<c>UpdatedBy</c>) and the tenant-ownership key
/// (<c>OwnedBy</c>) come from two independent sources instead of the one claim that feeds both today.
/// </summary>
/// <remarks>
/// See <see cref="AuthOnApiFixture" />'s remarks for why the early-bind env var is required here too.
/// </remarks>
public sealed class AuthOnFixedTenantApiFixture : TestApiFactoryBase, IAsyncLifetime
{
    private const string RequireAuthorizationEnvKey = "FeatureManagement__RequireAuthorization";
    private const string EnableDemoAuthenticationEnvKey = "FeatureManagement__EnableDemoAuthentication";

    /// <summary>The tenant-ownership key every request resolves to, regardless of the signed-in subject.</summary>
    public const string TenantKey = "5c81ab90-3d62-4f17-9e04-1b6f8a2d5347";

    public AuthOnFixedTenantApiFixture()
    {
        Environment.SetEnvironmentVariable(RequireAuthorizationEnvKey, "true");
        Environment.SetEnvironmentVariable(EnableDemoAuthenticationEnvKey, "false");
    }

    #region Methods

    protected override void ConfigureTestServices(IServiceCollection services)
    {
        base.ConfigureTestServices(services);
        MultiSubjectAuthHandler.Register(services);

        services.RemoveAll<IDataOwnerProvider>();
        services.AddScoped<IDataOwnerProvider>(_ => new FixedTenantDataOwnerProvider(TenantKey));
    }

    public async Task InitializeAsync()
    {
        _ = CreateClient();
        await ResetDatabaseAsync();
    }

    Task IAsyncLifetime.DisposeAsync() => Task.CompletedTask;

    protected override void Dispose(bool disposing)
    {
        Environment.SetEnvironmentVariable(RequireAuthorizationEnvKey, null);
        Environment.SetEnvironmentVariable(EnableDemoAuthenticationEnvKey, null);
        base.Dispose(disposing);
    }

    #endregion
}
