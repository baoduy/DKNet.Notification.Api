using DKNet.Notification.App.TestSupport;

namespace DKNet.Notification.App.Tests.Integration.Support;

/// <summary>
/// <see cref="ApiFixture" /> variant with <c>FeatureManagement:RequireAuthorization</c> AND
/// <c>FeatureManagement:EnableDemoAuthentication</c> both on — the conflicting combination the host must
/// refuse to start with (R2), never silently prefer one provider over the other.
/// </summary>
/// <remarks>
/// See <see cref="AuthOnApiFixture" />'s remarks for why these flags must be set as environment variables
/// rather than through <see cref="TestApiFactoryBase.AddFeatureOverrides" /> — <c>Program.cs</c> binds
/// <c>FeatureOptions</c> before that override is merged in. Unlike the other fixtures in this folder, a test
/// using this one is expected to fail host start-up, so it deliberately does not implement
/// <see cref="IAsyncLifetime" /> or call <c>ResetDatabaseAsync</c> — building the host is the act under test.
/// </remarks>
public sealed class RequireAuthorizationPlusDemoApiFixture : TestApiFactoryBase
{
    private const string RequireAuthorizationEnvKey = "FeatureManagement__RequireAuthorization";
    private const string EnableDemoAuthenticationEnvKey = "FeatureManagement__EnableDemoAuthentication";

    public RequireAuthorizationPlusDemoApiFixture()
    {
        Environment.SetEnvironmentVariable(RequireAuthorizationEnvKey, "true");
        Environment.SetEnvironmentVariable(EnableDemoAuthenticationEnvKey, "true");
    }

    #region Methods

    protected override void Dispose(bool disposing)
    {
        Environment.SetEnvironmentVariable(RequireAuthorizationEnvKey, null);
        Environment.SetEnvironmentVariable(EnableDemoAuthenticationEnvKey, null);
        base.Dispose(disposing);
    }

    #endregion
}
