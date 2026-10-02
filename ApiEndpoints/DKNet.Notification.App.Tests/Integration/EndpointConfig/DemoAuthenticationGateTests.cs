using DKNet.Notification.App.Tests.Integration.Support;
using DKNet.Notification.Share.Options;

namespace DKNet.Notification.App.Tests.Integration.EndpointConfig;

/// <summary>
/// <c>RequireAuthorization</c> and the built-in demonstration authentication provider are mutually exclusive
/// (R2): the demonstration identity is never a real caller, so a host that also demands real authorization
/// must refuse to start rather than silently prefer one over the other.
/// </summary>
public sealed class DemoAuthenticationGateTests
{
    #region Methods

    [Fact]
    public void RealAuthorizationPlusDemoProvider_RefusesToStart()
    {
        var exception = Should.Throw<InvalidOperationException>(() =>
        {
            using var fixture = new RequireAuthorizationPlusDemoApiFixture();
            fixture.CreateClient();
        });

        exception.Message.ShouldContain(nameof(FeatureOptions.RequireAuthorization));
        exception.Message.ShouldContain(nameof(FeatureOptions.EnableDemoAuthentication));
    }

    #endregion
}
