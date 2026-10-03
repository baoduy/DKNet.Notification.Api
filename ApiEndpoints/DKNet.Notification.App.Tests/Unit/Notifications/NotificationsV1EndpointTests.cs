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

    [Theory]
    [InlineData("TEMPLATE_NOT_FOUND", "templateId", "No template is registered with this id.")]
    [InlineData("RECIPIENT_MISSING", "to", "The email needs a recipient.")]
    [InlineData("RECIPIENT_INVALID", "to", "The recipient must be exactly 1 address of at most 254 characters.")]
    [InlineData("RECIPIENT_MISSING", "teamsDestination", "The Teams message needs a destination.")]
    [InlineData("RECIPIENT_INVALID", "teamsDestination", "The destination must be 1 to 64 lowercase letters, digits or '-'.")]
    [InlineData("PARAMETER_MISSING", "parameters.accountNumber", "A template token has no parameter.")]
    [InlineData("MESSAGE_TOO_LARGE", "", "The Teams message would be larger than 28,672 bytes.")]
    [InlineData("QUEUE_FULL", "", "The delivery queue is full. Try again later.")]
    public void Each_refusal_names_its_reason(string code, string field, string message) =>
        NotificationsV1Endpoint.ErrorMessage(code, field).ShouldBe(message);
}
