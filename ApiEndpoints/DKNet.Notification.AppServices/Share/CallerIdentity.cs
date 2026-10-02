using System.Security.Claims;
using DKNet.Notification.Share;

namespace DKNet.Notification.AppServices.Share;

/// <summary>
///     The id of the calling application: the key every idempotency record is scoped by, and the caller in every
///     notification log entry.
/// </summary>
public static class CallerIdentity
{
    #region Fields

    /// <summary>The caller claims, in the order the first non-empty one wins.</summary>
    private static readonly string[] CallerClaimTypes = ["client_id", "azp", "appid"];

    #endregion

    #region Methods

    /// <summary>Resolves the caller id of <paramref name="user" />.</summary>
    /// <param name="user">The request's user.</param>
    /// <returns>
    ///     The first non-empty <c>client_id</c>, <c>azp</c> or <c>appid</c> claim; <see cref="SharedConsts.SystemAccount" />
    ///     for a caller that is not signed in (sign-in off); <see langword="null" /> for a signed-in token that carries
    ///     none of the 3 claims.
    /// </returns>
    public static string? Resolve(ClaimsPrincipal user)
    {
        ArgumentNullException.ThrowIfNull(user);

        if (user.Identity?.IsAuthenticated != true)
        {
            return SharedConsts.SystemAccount;
        }

        return CallerClaimTypes
            .SelectMany(user.FindAll)
            .Select(claim => claim.Value)
            .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
    }

    #endregion
}
