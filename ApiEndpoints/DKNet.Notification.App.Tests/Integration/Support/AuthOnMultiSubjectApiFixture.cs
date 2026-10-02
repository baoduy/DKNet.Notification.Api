using DKNet.Notification.App.TestSupport;

namespace DKNet.Notification.App.Tests.Integration.Support;

/// <summary>
/// <see cref="ApiFixture" /> variant with <c>FeatureManagement:RequireAuthorization</c> flipped on, like
/// <see cref="AuthOnApiFixture" />, but authenticates via <see cref="MultiSubjectAuthHandler" /> instead —
/// each request can carry a different caller's subject/oid claims via headers, so tests can prove isolation
/// between two DIFFERENT authenticated callers sharing the same host and database.
/// </summary>
/// <remarks>
/// See <see cref="AuthOnApiFixture" />'s remarks for why the early-bind env var is required here too.
/// </remarks>
public sealed class AuthOnMultiSubjectApiFixture : TestApiFactoryBase, IAsyncLifetime
{
    private const string RequireAuthorizationEnvKey = "FeatureManagement__RequireAuthorization";
    private const string EnableDemoAuthenticationEnvKey = "FeatureManagement__EnableDemoAuthentication";

    public AuthOnMultiSubjectApiFixture()
    {
        Environment.SetEnvironmentVariable(RequireAuthorizationEnvKey, "true");
        Environment.SetEnvironmentVariable(EnableDemoAuthenticationEnvKey, "false");
    }

    #region Methods

    protected override void ConfigureTestServices(IServiceCollection services)
    {
        base.ConfigureTestServices(services);
        MultiSubjectAuthHandler.Register(services);
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
