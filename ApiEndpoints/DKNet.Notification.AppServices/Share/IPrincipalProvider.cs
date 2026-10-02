using DKNet.EfCore.AuditLogs;
using DKNet.EfCore.DataAuthorization;

namespace DKNet.Notification.AppServices.Share;

public interface IPrincipalProvider : IDataOwnerProvider, ICurrentUserProvider
{
    #region Properties

    /// <summary>
    ///     The User Id from Bearer Token
    /// </summary>
    /// <remarks>
    ///     Is <see cref="Guid.Empty" /> when the caller's subject claim is not a GUID (e.g. an Entra v2.0
    ///     pairwise <c>sub</c>). <see cref="IDataOwnerProvider.GetOwnershipKey" /> — not this property — is the
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
}