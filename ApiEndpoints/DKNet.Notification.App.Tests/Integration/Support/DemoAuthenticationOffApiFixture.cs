using DKNet.Notification.App.TestSupport;

namespace DKNet.Notification.App.Tests.Integration.Support;

/// <summary>
/// <see cref="ApiFixture" /> variant with <c>FeatureManagement:EnableDemoAuthentication</c> flipped off, so the
/// caller stays genuinely unauthenticated even though the Testing overlay enables the built-in demonstration
/// provider by default — proves the true-anonymous path (<see cref="ProductSensitiveDataAnonymousTests" />)
/// independently of that default.
/// </summary>
/// <remarks>
/// See <see cref="AuthOnApiFixture" />'s remarks for why the early-bind env var is required here too.
/// </remarks>
public sealed class DemoAuthenticationOffApiFixture : TestApiFactoryBase, IAsyncLifetime
{
    private const string EnableDemoAuthenticationEnvKey = "FeatureManagement__EnableDemoAuthentication";

    public DemoAuthenticationOffApiFixture() =>
        Environment.SetEnvironmentVariable(EnableDemoAuthenticationEnvKey, "false");

    #region Methods

    public async Task InitializeAsync()
    {
        _ = CreateClient();
        await ResetDatabaseAsync();
    }

    Task IAsyncLifetime.DisposeAsync() => Task.CompletedTask;

    protected override void Dispose(bool disposing)
    {
        Environment.SetEnvironmentVariable(EnableDemoAuthenticationEnvKey, null);
        base.Dispose(disposing);
    }

    #endregion
}
