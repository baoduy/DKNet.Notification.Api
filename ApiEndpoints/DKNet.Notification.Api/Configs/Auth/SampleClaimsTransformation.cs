using Microsoft.AspNetCore.Authentication;

namespace DKNet.Notification.Api.Configs.Auth;

/// <summary>
///     Sample implementation of <see cref="IClaimsTransformation" /> that demonstrates how to enrich or transform
///     claims on the current user principal.
/// </summary>
/// <remarks>
///     <para>
///         Register this class with the DI container via
///         <c>services.AddScoped&lt;IClaimsTransformation, SampleClaimsTransformation&gt;()</c>
///         and ASP.NET Core will automatically invoke it on each authentication event.
///     </para>
///     <para>
///         Use this as a starting point to add roles, normalize claim types, or enrich the principal
///         with data fetched from a database or cache (inject dependencies via the constructor).
///     </para>
///     <para>
///         <b>Important:</b> ASP.NET Core may call <see cref="TransformAsync" /> more than once per request.
///         Always guard against duplicate claims before adding them.
///     </para>
/// </remarks>
[ExcludeFromCodeCoverage]
internal sealed class SampleClaimsTransformation : IClaimsTransformation
{
    #region Methods

    /// <summary>
    ///     Transforms the given <paramref name="principal" /> by adding or modifying claims.
    /// </summary>
    /// <param name="principal">The <see cref="ClaimsPrincipal" /> to transform.</param>
    /// <returns>
    ///     A <see cref="Task{ClaimsPrincipal}" /> containing the (optionally enriched) principal.
    /// </returns>
    /// <remarks>
    ///     The method adds additional claims to a new <see cref="ClaimsIdentity" /> which is then
    ///     attached to the existing principal.  Returning the original principal unchanged when no
    ///     enrichment is needed keeps the implementation efficient.
    /// </remarks>
    public Task<ClaimsPrincipal> TransformAsync(ClaimsPrincipal principal)
    {
        // Skip transformation for unauthenticated principals.
        if (principal.Identity?.IsAuthenticated != true)
        {
            return Task.FromResult(principal);
        }

        var additionalClaims = new ClaimsIdentity();

        // TODO: Replace the sample below with real enrichment logic, for example:
        //   - Fetch roles/permissions from a database or distributed cache for the current user.
        //   - Normalise provider-specific claim types to application-defined claim types.
        //   - Add tenant or organisation identifiers derived from the token or an external store.
        const string sampleClaimType = "sample-claim";

        if (!principal.HasClaim(c =>
                string.Equals(c.Type, sampleClaimType, StringComparison.OrdinalIgnoreCase)))
        {
            additionalClaims.AddClaim(new Claim(sampleClaimType, "sample-value"));
        }

        // Only attach the new identity when there are claims to add.
        if (additionalClaims.Claims.Any())
        {
            principal.AddIdentity(additionalClaims);
        }

        return Task.FromResult(principal);
    }

    #endregion
}
