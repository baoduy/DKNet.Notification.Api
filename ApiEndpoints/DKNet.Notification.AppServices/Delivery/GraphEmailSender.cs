using Azure.Core;

namespace DKNet.Notification.AppServices.Delivery;

/// <summary>
///     The Microsoft Graph sender (ADR-0009): each attempt gets a token, then makes one <c>sendMail</c> call on the
///     settings' mailbox, with the rendered subject, the HTML body and the one <c>to</c> address only.
/// </summary>
public sealed class GraphEmailSender : IDeliverySender
{
    #region Constructors

    /// <param name="email">Email settings with the sender <c>Graph</c> and good Graph settings.</param>
    /// <param name="credential">The credential from <see cref="GraphSignIn.Credential" />.</param>
    /// <param name="http">The client the send goes through; it follows no redirect.</param>
    /// <param name="endpoints">Where to send.</param>
    public GraphEmailSender(
        EmailChannelSettings email,
        TokenCredential credential,
        HttpClient http,
        GraphEndpoints endpoints) =>
        throw new NotImplementedException();

    #endregion

    #region Methods

    /// <inheritdoc />
    public Task<DeliveryFailure?> SendAsync(
        Domains.Notifications.Notification notification,
        CancellationToken stoppingToken) =>
        throw new NotImplementedException();

    #endregion
}
