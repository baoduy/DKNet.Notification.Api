using System.Diagnostics.CodeAnalysis;
using System.Net.Http.Headers;
using System.Security.Cryptography.X509Certificates;

namespace DKNet.Notification.AppServices.Delivery;

/// <summary>
///     Makes one delivery attempt of a <c>teams</c> notification: 1 HTTPS POST of its card to the destination's
///     webhook, within the Teams time limit, never following a redirect.
/// </summary>
public sealed class TeamsWebhookSender : IDeliverySender, IDisposable
{
    #region Fields

    /// <summary>The key the sender is registered under: the channel it delivers.</summary>
    public const string ChannelKey = "teams";

    // A destination with no webhook: no attempt can reach it.
    private static readonly DeliveryFailure NoWebhook = new(IsTransient: false, ReplyCode: string.Empty);

    private readonly HttpClient _http;
    private readonly TeamsChannelSettings _settings;

    #endregion

    #region Constructors

    /// <summary>Creates the sender of the Teams destinations in <paramref name="settings" />.</summary>
    /// <param name="settings">The Teams settings, read once at start-up.</param>
    /// <param name="trustedRoots">Certificate authorities trusted on top of the machine's own; empty in the release.</param>
    [SuppressMessage(
        "Reliability",
        "CA2000:Dispose objects before losing scope",
        Justification = "The sender owns the transport through its client and disposes it with the client.")]
    public TeamsWebhookSender(TeamsChannelSettings settings, TeamsTrustedRoots trustedRoots)
        : this(settings, GraphEndpoints.CreateTransport(RootsOf(trustedRoots)))
    {
    }

    /// <param name="settings">The Teams settings.</param>
    /// <param name="transport">The handler the POST goes through; the sender owns it.</param>
    internal TeamsWebhookSender(TeamsChannelSettings settings, HttpMessageHandler transport)
    {
        ArgumentNullException.ThrowIfNull(settings);
        _settings = settings;
        // The attempt's own time limit replaces the client's.
        _http = new HttpClient(transport) { Timeout = Timeout.InfiniteTimeSpan };
    }

    #endregion

    #region Methods

    /// <inheritdoc />
    /// <remarks>
    ///     Only the HTTP status of the answer is kept, never its text, and the webhook URL is never logged: its query
    ///     string holds the signature.
    /// </remarks>
    public async Task<DeliveryFailure?> SendAsync(
        Domains.Notifications.Notification notification,
        CancellationToken stoppingToken)
    {
        ArgumentNullException.ThrowIfNull(notification);
        var rendered = notification.RenderedMessage
                       ?? throw new InvalidOperationException("Only a queued notification has a message to send.");
        var recipient = notification.TeamsRecipient
                        ?? throw new InvalidOperationException("Only a queued Teams notification has a destination.");
        if (_settings.WebhookFor(recipient.Name) is not { } webhook)
        {
            return NoWebhook;
        }

        using var attempt = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
        attempt.CancelAfter(TimeSpan.FromSeconds(_settings.TimeoutSeconds));
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, webhook);
            request.Content = new ByteArrayContent(TeamsCard.Serialize(rendered));
            request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
            using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, attempt.Token);
            return response.IsSuccessStatusCode
                ? null
                : GraphEmailSender.Answer((int)response.StatusCode, response.Headers);
        }
        catch (Exception error) when (!stoppingToken.IsCancellationRequested && GraphEmailSender.FailureOf(error) is { } failure)
        {
            return failure;
        }
    }

    /// <inheritdoc />
    public void Dispose() => _http.Dispose();

    private static IReadOnlyCollection<X509Certificate2> RootsOf(TeamsTrustedRoots trustedRoots)
    {
        ArgumentNullException.ThrowIfNull(trustedRoots);
        return trustedRoots.Certificates;
    }

    #endregion
}
