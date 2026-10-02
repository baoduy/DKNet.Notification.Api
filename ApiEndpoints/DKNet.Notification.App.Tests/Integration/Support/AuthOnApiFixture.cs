using DKNet.Notification.App.TestSupport;

namespace DKNet.Notification.App.Tests.Integration.Support;

/// <summary>
/// <see cref="ApiFixture" /> variant with <c>FeatureManagement:RequireAuthorization</c> flipped on and the
/// real JWT bearer scheme replaced by <see cref="TestAuthHandler" />, so requests are authenticated as
/// <see cref="TestAuthHandler.CallerName" /> without needing a live token.
/// </summary>
/// <remarks>
/// <c>Program.cs</c> binds <c>FeatureOptions</c> from configuration in its very first lines, before
/// <see cref="TestApiFactoryBase.ConfigureWebHost" />'s <c>ConfigureAppConfiguration</c> override is merged in
/// (the same constraint <c>BddApiFactory</c> documents for the Redis connection string) — so a plain
/// <see cref="AddFeatureOverrides" /> entry cannot flip a flag that early-bound code branches on. The
/// environment variable set in the constructor is read while <c>WebApplication.CreateBuilder(args)</c> itself
/// builds the initial configuration, ahead of that early bind, so it is what actually takes effect. This is
/// safe only because this assembly disables collection parallelization (<c>AssemblyInfo.cs</c>) — no other
/// test's host can boot while the variable is set.
/// </remarks>
public sealed class AuthOnApiFixture : TestApiFactoryBase, IAsyncLifetime
{
    private const string RequireAuthorizationEnvKey = "FeatureManagement__RequireAuthorization";

    public AuthOnApiFixture()
    {
        Environment.SetEnvironmentVariable(RequireAuthorizationEnvKey, "true");
    }

    #region Methods

    protected override void ConfigureTestServices(IServiceCollection services)
    {
        base.ConfigureTestServices(services);
        TestAuthHandler.Register(services);
    }

    public Task InitializeAsync()
    {
        _ = CreateClient();
        return Task.CompletedTask;
    }

    Task IAsyncLifetime.DisposeAsync() => Task.CompletedTask;

    protected override void Dispose(bool disposing)
    {
        Environment.SetEnvironmentVariable(RequireAuthorizationEnvKey, null);
        base.Dispose(disposing);
    }

    #endregion
}
