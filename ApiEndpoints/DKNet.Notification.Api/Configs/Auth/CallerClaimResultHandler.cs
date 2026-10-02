using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Policy;

namespace DKNet.Notification.Api.Configs.Auth;

/// <summary>
///     Answers 401, not 403, to a signed-in token that names no calling application: DRK-2013 treats such a token
///     as invalid. Every other outcome keeps the framework's answer.
/// </summary>
internal sealed class CallerClaimResultHandler : IAuthorizationMiddlewareResultHandler
{
    #region Fields

    private readonly AuthorizationMiddlewareResultHandler _default = new();

    #endregion

    #region Methods

    public Task HandleAsync(
        RequestDelegate next,
        HttpContext context,
        AuthorizationPolicy policy,
        PolicyAuthorizationResult authorizeResult) =>
        authorizeResult.Forbidden && CallerIdentity.Resolve(context.User) is null
            ? context.ChallengeAsync()
            : _default.HandleAsync(next, context, policy, authorizeResult);

    #endregion
}
