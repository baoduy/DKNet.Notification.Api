using System.Security.Cryptography.X509Certificates;

namespace DKNet.Notification.AppServices.Delivery;

/// <summary>
///     Where the Graph sender signs in and sends: Microsoft's global cloud in the release (<see cref="Global" />).
///     Bound to no setting: only a test host replaces it, to point the sender at its local Graph and token stubs.
/// </summary>
/// <param name="graphAddress">The Microsoft Graph base address; <c>sendMail</c> is under <c>v1.0/users/{Mailbox}</c>.</param>
/// <param name="authorityHost">The Microsoft Entra ID sign-in address; the token request is for the settings' tenant.</param>
/// <param name="trustedRoots">Certificate authorities trusted on top of the machine's own; empty in the release.</param>
/// <param name="serviceAccountTokenFile">
///     The service account token file of workload identity; <see langword="null" /> in the release, where the
///     credential's own default applies.
/// </param>
public sealed class GraphEndpoints(
    Uri graphAddress,
    Uri authorityHost,
    IReadOnlyCollection<X509Certificate2> trustedRoots,
    string? serviceAccountTokenFile)
{
    #region Fields

    /// <summary>Microsoft's global cloud: the only addresses the release uses (DRK-2028 decision 4).</summary>
    public static readonly GraphEndpoints Global = new(
        new Uri("https://graph.microsoft.com"),
        new Uri("https://login.microsoftonline.com/"),
        [],
        serviceAccountTokenFile: null);

    #endregion

    #region Properties

    /// <summary>Gets the Microsoft Graph base address.</summary>
    public Uri GraphAddress { get; } = graphAddress;

    /// <summary>Gets the Microsoft Entra ID sign-in address.</summary>
    public Uri AuthorityHost { get; } = authorityHost;

    /// <summary>Gets the extra trusted authorities.</summary>
    public IReadOnlyCollection<X509Certificate2> TrustedRoots { get; } = trustedRoots;

    /// <summary>Gets the service account token file of workload identity, or <see langword="null" />.</summary>
    public string? ServiceAccountTokenFile { get; } = serviceAccountTokenFile;

    #endregion

    #region Methods

    /// <summary>
    ///     A new handler for the sign-in and the send: it follows no redirect, and trusts <see cref="TrustedRoots" />
    ///     on top of the machine's own authorities.
    /// </summary>
    public HttpMessageHandler CreateTransport() => CreateTransport(TrustedRoots);

    /// <summary>
    ///     A new handler for an outbound HTTPS call: it follows no redirect, and trusts <paramref name="trustedRoots" />
    ///     on top of the machine's own authorities. The Graph and Teams senders both go through it.
    /// </summary>
    /// <param name="trustedRoots">Certificate authorities trusted on top of the machine's own; empty in the release.</param>
    internal static HttpMessageHandler CreateTransport(IReadOnlyCollection<X509Certificate2> trustedRoots)
    {
        var handler = new SocketsHttpHandler { AllowAutoRedirect = false };
        if (trustedRoots.Count > 0)
        {
            handler.SslOptions.RemoteCertificateValidationCallback = (_, certificate, _, errors) =>
                SmtpEmailSender.IsTrusted(certificate, errors, trustedRoots);
        }

        return handler;
    }

    #endregion
}
