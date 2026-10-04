using System.Security.Claims;
using DKNet.Notification.Api.Configs.Auth;

namespace DKNet.Notification.App.Tests.Unit.Notifications;

/// <summary>DRK-2013 step 1: notifications.send from scp or scope (space-separated) or roles (one per claim).</summary>
public sealed class SendPermissionTests
{
    private static ClaimsPrincipal Token(params (string Type, string Value)[] claims) =>
        new(new ClaimsIdentity(claims.Select(c => new Claim(c.Type, c.Value)), "TestScheme"));

    [Theory]
    [InlineData("scp", "user.read notifications.send")]
    [InlineData("scope", "notifications.send user.read")]
    [InlineData("roles", "notifications.send")]
    public void The_permission_is_read_from_its_3_claims(string claim, string value) =>
        SendPermission.IsGranted(Token(("client_id", "card-ops"), (claim, value))).ShouldBeTrue();

    [Fact]
    public void A_second_roles_claim_carries_the_permission() =>
        SendPermission.IsGranted(Token(("client_id", "card-ops"), ("roles", "notifications.read"), ("roles", "notifications.send")))
            .ShouldBeTrue();

    [Theory]
    [InlineData("scp", "notifications.read")]
    [InlineData("scp", "notifications.sender")]
    [InlineData("scp", "Notifications.Send")]
    [InlineData("roles", "notifications.read notifications.send")]
    [InlineData("other", "notifications.send")]
    public void Any_other_claim_or_value_is_not_the_permission(string claim, string value) =>
        SendPermission.IsGranted(Token(("client_id", "card-ops"), (claim, value))).ShouldBeFalse();

    [Fact]
    public void A_token_with_no_caller_claim_is_not_granted() =>
        SendPermission.IsGranted(Token(("roles", "notifications.send"))).ShouldBeFalse();
}
