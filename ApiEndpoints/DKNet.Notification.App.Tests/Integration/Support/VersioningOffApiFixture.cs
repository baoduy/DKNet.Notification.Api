using DKNet.Notification.App.TestSupport;

namespace DKNet.Notification.App.Tests.Integration.Support;

/// <summary>
/// <see cref="ApiFixture" /> variant with <c>FeatureManagement:EnableVersioning</c> flipped off, so groups map
/// to the unversioned route (no <c>{version:apiVersion}</c> segment).
/// </summary>
/// <remarks>
/// Uses an environment variable rather than <see cref="TestApiFactoryBase.AddFeatureOverrides" /> for the same
/// early-bind reason documented on <see cref="AuthOnApiFixture" />.
/// </remarks>
public sealed class VersioningOffApiFixture : TestApiFactoryBase, IAsyncLifetime
{
    private const string EnableVersioningEnvKey = "FeatureManagement__EnableVersioning";

    public VersioningOffApiFixture() => Environment.SetEnvironmentVariable(EnableVersioningEnvKey, "false");

    #region Methods

    public async Task InitializeAsync()
    {
        _ = CreateClient();
        await ResetDatabaseAsync();
    }

    Task IAsyncLifetime.DisposeAsync() => Task.CompletedTask;

    protected override void Dispose(bool disposing)
    {
        Environment.SetEnvironmentVariable(EnableVersioningEnvKey, null);
        base.Dispose(disposing);
    }

    #endregion
}
