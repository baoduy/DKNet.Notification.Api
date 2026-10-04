using System.Security.Claims;
using DKNet.Notification.AppServices.Share;

namespace DKNet.Notification.App.Tests.Unit.Notifications;

/// <summary>DRK-2013 step 1: the caller id is the first non-empty of client_id, azp and appid.</summary>
public sealed class CallerIdentityTests
{
    private static ClaimsPrincipal SignedIn(params (string Type, string Value)[] claims) =>
        new(new ClaimsIdentity(claims.Select(c => new Claim(c.Type, c.Value)), "TestScheme"));

    [Fact]
    public void A_caller_that_is_not_signed_in_is_System() =>
        CallerIdentity.Resolve(new ClaimsPrincipal(new ClaimsIdentity([new Claim("client_id", "treasury-ops")])))
            .ShouldBe("System");

    [Fact]
    public void A_missing_user_is_refused() =>
        Should.Throw<ArgumentNullException>(() => CallerIdentity.Resolve(null!)).ParamName.ShouldBe("user");

    [Fact]
    public void A_caller_with_no_identity_is_System() =>
        CallerIdentity.Resolve(new ClaimsPrincipal()).ShouldBe("System");

    [Theory]
    [InlineData("client_id", "treasury-ops")]
    [InlineData("azp", "console-app")]
    [InlineData("appid", "legacy-app")]
    public void Each_caller_claim_names_the_caller(string type, string value) =>
        CallerIdentity.Resolve(SignedIn((type, value))).ShouldBe(value);

    [Fact]
    public void The_claim_order_of_the_spec_wins_over_the_token_order() =>
        CallerIdentity.Resolve(SignedIn(("appid", "legacy-app"), ("azp", "console-app"), ("client_id", "treasury-ops")))
            .ShouldBe("treasury-ops");

    [Fact]
    public void Azp_wins_over_appid() =>
        CallerIdentity.Resolve(SignedIn(("appid", "legacy-app"), ("azp", "console-app"))).ShouldBe("console-app");

    [Fact]
    public void An_empty_caller_claim_is_passed_over() =>
        CallerIdentity.Resolve(SignedIn(("client_id", " "), ("azp", "console-app"))).ShouldBe("console-app");

    [Fact]
    public void A_signed_in_token_with_no_caller_claim_has_no_caller() =>
        CallerIdentity.Resolve(SignedIn(("roles", "notifications.send"), ("sub", "someone"))).ShouldBeNull();
}
