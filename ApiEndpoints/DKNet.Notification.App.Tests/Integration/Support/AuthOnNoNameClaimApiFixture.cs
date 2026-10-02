using DKNet.Notification.App.TestSupport;

namespace DKNet.Notification.App.Tests.Integration.Support;

/// <summary>
/// <see cref="ApiFixture" /> variant with <c>FeatureManagement:RequireAuthorization</c> flipped on, like
/// <see cref="AuthOnApiFixture" />, but authenticates every request via <see cref="NoNameClaimAuthHandler" />
/// instead — a caller who is authenticated but whose token carries no <see cref="System.Security.Claims.ClaimTypes.Name" />
/// claim. Proves the missing-claim-while-authenticated path: the declared member holds its default
/// (<see langword="null" />).
/// </summary>
/// <remarks>
/// See <see cref="AuthOnApiFixture" />'s remarks for why the early-bind env var is required here too.
/// </remarks>
public sealed class AuthOnNoNameClaimApiFixture : TestApiFactoryBase, IAsyncLifetime
{
    private const string RequireAuthorizationEnvKey = "FeatureManagement__RequireAuthorization";
    private const string EnableDemoAuthenticationEnvKey = "FeatureManagement__EnableDemoAuthentication";

    public AuthOnNoNameClaimApiFixture()
    {
        Environment.SetEnvironmentVariable(RequireAuthorizationEnvKey, "true");
        Environment.SetEnvironmentVariable(EnableDemoAuthenticationEnvKey, "false");
    }

    #region Methods

    protected override void ConfigureTestServices(IServiceCollection services)
    {
        base.ConfigureTestServices(services);
        NoNameClaimAuthHandler.Register(services);
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
