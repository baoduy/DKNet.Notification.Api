using Microsoft.AspNetCore.Authorization;

namespace DKNet.Notification.Api.Configs.Auth;

/// <summary>
///     Sample authorization requirement that demonstrates how to implement
///     <see cref="IAuthorizationRequirement" />.
/// </summary>
/// <remarks>
///     <para>
///         An <see cref="IAuthorizationRequirement" /> describes the condition that must be satisfied for a
///         policy to succeed.  Pair it with a corresponding <see cref="IAuthorizationHandler" /> (see
///         <see cref="HasScopeHandler" />) and register the policy in <see cref="AuthConfig" />.
///     </para>
///     <para>
///         Apply the policy to an endpoint or route group with
///         <c>.RequireAuthorization(<see cref="PolicyName" />)</c>.
///     </para>
/// </remarks>
/// <param name="requiredScope">The JWT scope value the user must possess to satisfy this requirement.</param>
[ExcludeFromCodeCoverage]
internal sealed class HasScopeRequirement(string requiredScope) : IAuthorizationRequirement
{
    #region Properties

    /// <summary>
    ///     The name of the sample authorization policy registered in <see cref="AuthConfig" />.
    /// </summary>
    /// <remarks>
    ///     Use this constant with <c>RequireAuthorization(HasScopeRequirement.PolicyName)</c>
    ///     to protect an endpoint or a route group with the sample scope policy.
    /// </remarks>
    public const string PolicyName = "SampleScopePolicy";

    /// <summary>Gets the JWT scope value that the user must possess to satisfy this requirement.</summary>
    public string RequiredScope { get; } = requiredScope;

    #endregion
}

/// <summary>
///     Authorization handler that evaluates <see cref="HasScopeRequirement" />.
/// </summary>
/// <remarks>
///     <para>
///         Extend this class to inject additional services (e.g. a repository) via the constructor
///         when the authorization decision requires data beyond the current user's claims.
///     </para>
///     <para>
///         Register with DI as
///         <c>services.AddScoped&lt;IAuthorizationHandler, HasScopeHandler&gt;()</c>.
///     </para>
/// </remarks>
[ExcludeFromCodeCoverage]
internal sealed class HasScopeHandler : AuthorizationHandler<HasScopeRequirement>
{
    #region Methods

    /// <inheritdoc />
    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        HasScopeRequirement requirement)
    {
        // JWT tokens issued by Azure AD / Entra ID use "scp" for delegated scopes.
        // Other providers may use "scope" – check both for portability.
        var scopeClaim = context.User.FindFirst(c =>
            c.Type.Equals("scp", StringComparison.OrdinalIgnoreCase) ||
            c.Type.Equals("scope", StringComparison.OrdinalIgnoreCase));

        if (scopeClaim is null)
        {
            // No scope claim present – leave the requirement unsatisfied.
            return Task.CompletedTask;
        }

        // A single scope claim may contain multiple space-separated values.
        var scopes = scopeClaim.Value.Split(' ', StringSplitOptions.RemoveEmptyEntries);

        if (scopes.Any(s => s.Equals(requirement.RequiredScope, StringComparison.OrdinalIgnoreCase)))
        {
            context.Succeed(requirement);
        }

        return Task.CompletedTask;
    }

    #endregion
}
