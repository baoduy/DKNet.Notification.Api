namespace DKNet.Notification.Api.Configs.Auth;

/// <summary>
///     The <c>notifications.send</c> permission (DRK-2013 step 1): a caller claim, and the permission in the
///     <c>scp</c> or <c>scope</c> list (space-separated) or in a <c>roles</c> claim (one value per claim).
/// </summary>
internal static class SendPermission
{
    #region Fields

    /// <summary>The permission, and the name of its policy.</summary>
    public const string Name = "notifications.send";

    #endregion

    #region Methods

    /// <summary>Whether <paramref name="user" /> may send notifications.</summary>
    /// <remarks>A token with no caller claim fails here; <see cref="CallerClaimResultHandler" /> answers it 401.</remarks>
    public static bool IsGranted(ClaimsPrincipal user) =>
        CallerIdentity.Resolve(user) is not null &&
        (user.HasClaim("roles", Name) ||
         user.Claims
             .Where(claim => claim.Type is "scp" or "scope")
             .Any(claim => claim.Value.Split(' ', StringSplitOptions.RemoveEmptyEntries).Contains(Name, StringComparer.Ordinal)));

    #endregion
}
