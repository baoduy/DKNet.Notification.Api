using System.Security.Cryptography.X509Certificates;

namespace DKNet.Notification.AppServices.Delivery;

/// <summary>
///     Certificate authorities the Teams sender trusts on top of the machine's own. Empty in the release, and bound to
///     no setting: only a test host adds its test authority, so no setting can turn the certificate check off.
/// </summary>
/// <param name="certificates">The extra trusted authorities.</param>
public sealed class TeamsTrustedRoots(IReadOnlyCollection<X509Certificate2> certificates)
{
    #region Properties

    /// <summary>Gets the extra trusted authorities.</summary>
    public IReadOnlyCollection<X509Certificate2> Certificates { get; } = certificates;

    #endregion
}
