using System.Security.Claims;
using DKNet.Notification.Api.Configs.Auth;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Policy;
using Microsoft.AspNetCore.Http;
using Moq;

namespace DKNet.Notification.App.Tests.Unit.Notifications;

/// <summary>DRK-2013 step 1: a signed-in token that names no calling application answers 401, not 403.</summary>
public sealed class CallerClaimResultHandlerTests
{
    private static readonly AuthorizationPolicy Policy = new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build();

    private readonly Mock<IAuthenticationService> _authentication = new();
    private bool _nextRan;

    private DefaultHttpContext Context(params (string Type, string Value)[] claims) => new()
    {
        User = new ClaimsPrincipal(new ClaimsIdentity(claims.Select(c => new Claim(c.Type, c.Value)), "TestScheme")),
        RequestServices = new ServiceCollection().AddSingleton(_authentication.Object).BuildServiceProvider()
    };

    private Task Handle(HttpContext context, PolicyAuthorizationResult result) =>
        new CallerClaimResultHandler().HandleAsync(_ => { _nextRan = true; return Task.CompletedTask; }, context, Policy, result);

    [Fact]
    public async Task A_refused_token_with_no_caller_claim_is_challenged()
    {
        var context = Context(("roles", "notifications.send"));

        await Handle(context, PolicyAuthorizationResult.Forbid());

        _authentication.Verify(a => a.ChallengeAsync(context, null, null), Times.Once);
        _authentication.Verify(a => a.ForbidAsync(context, It.IsAny<string?>(), It.IsAny<AuthenticationProperties?>()), Times.Never);
        _nextRan.ShouldBeFalse();
    }

    [Fact]
    public async Task A_refused_token_with_a_caller_claim_is_forbidden()
    {
        var context = Context(("client_id", "card-ops"), ("scp", "notifications.read"));

        await Handle(context, PolicyAuthorizationResult.Forbid());

        _authentication.Verify(a => a.ForbidAsync(context, null, null), Times.Once);
        _authentication.Verify(a => a.ChallengeAsync(context, It.IsAny<string?>(), It.IsAny<AuthenticationProperties?>()), Times.Never);
        _nextRan.ShouldBeFalse();
    }

    [Fact]
    public async Task An_allowed_call_runs_on()
    {
        await Handle(Context(("client_id", "card-ops")), PolicyAuthorizationResult.Success());

        _nextRan.ShouldBeTrue();
        _authentication.VerifyNoOtherCalls();
    }
}
