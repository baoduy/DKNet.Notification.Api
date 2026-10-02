using System.Security.Claims;
using DKNet.Notification.Api.ApiEndpoints.Notifications;
using Microsoft.AspNetCore.Http;

namespace DKNet.Notification.App.Tests.Unit.Notifications;

/// <summary>The caller id the send endpoint passes on; the send scenarios cover the rest of the route.</summary>
public sealed class NotificationsV1EndpointTests
{
    [Fact]
    public void The_caller_id_comes_from_the_caller_claim() =>
        NotificationsV1Endpoint.CallerOf(new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity([new Claim("azp", "console-app")], "TestScheme"))
        }).ShouldBe("console-app");

    [Fact]
    public void A_call_with_no_caller_claim_never_runs_a_step() =>
        Should.Throw<InvalidOperationException>(() => NotificationsV1Endpoint.CallerOf(new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity([new Claim("roles", "notifications.send")], "TestScheme"))
        })).Message.ShouldBe("The call carries no caller claim.");
}
