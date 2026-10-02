namespace DKNet.Notification.AppServices.Share;

/// <summary>
///     The authenticated caller of the current request, read from its bearer token claims.
/// </summary>
public interface IPrincipalProvider
{
    #region Properties

    /// <summary>
    ///     The User Id from Bearer Token
    /// </summary>
    /// <remarks>
    ///     Is <see cref="Guid.Empty" /> when the caller's subject claim is not a GUID (e.g. an Entra v2.0
    ///     pairwise <c>sub</c>). <see cref="GetOwnershipKey" /> — not this property — is the
    ///     authorization boundary.
    /// </remarks>
    Guid ProfileId { get; }

    /// <summary>
    ///     User Email from Bearer Token
    /// </summary>
    string Email { get; }

    /// <summary>
    ///     User name from Bearer Token
    /// </summary>
    string UserName { get; }

    #endregion

    #region Methods

    /// <summary>
    ///     The caller's first non-empty subject claim (object id, name identifier or <c>sub</c>), or
    ///     <see cref="SharedConsts.SystemAccount" /> for an anonymous caller.
    /// </summary>
    string? GetOwnershipKey();

    /// <summary>
    ///     The acting user's key; today the same claim as <see cref="GetOwnershipKey" />.
    /// </summary>
    string? GetCurrentUser();

    #endregion
}