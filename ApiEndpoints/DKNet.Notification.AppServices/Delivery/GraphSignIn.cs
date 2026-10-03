using Azure.Core;

namespace DKNet.Notification.AppServices.Delivery;

/// <summary>The Graph sender's sign-in to Microsoft Entra ID as the mail-sender app (ADR-0010).</summary>
public static class GraphSignIn
{
    #region Methods

    /// <summary>
    ///     The one credential of the Graph sender: workload identity or the client secret, as
    ///     <see cref="GraphSenderSettings.Credential" /> says, for the settings' tenant and app at
    ///     <see cref="GraphEndpoints.AuthorityHost" />. It never retries by itself and keeps its token until close to
    ///     expiry.
    /// </summary>
    /// <param name="settings">Good Graph settings.</param>
    /// <param name="endpoints">Where to sign in.</param>
    /// <param name="transport">The handler every token request goes through.</param>
    public static TokenCredential Credential(
        GraphSenderSettings settings,
        GraphEndpoints endpoints,
        HttpMessageHandler transport) =>
        throw new NotImplementedException();

    #endregion
}
