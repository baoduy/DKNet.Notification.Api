using System.Globalization;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Net.Sockets;
using Azure.Core;
using Azure.Identity;
using Microsoft.Identity.Client;

namespace DKNet.Notification.AppServices.Delivery;

/// <summary>
///     The Microsoft Graph sender (ADR-0009): each attempt gets a token, then makes one <c>sendMail</c> call on the
///     settings' mailbox, with the rendered subject, the HTML body and the one <c>to</c> address only.
/// </summary>
public sealed class GraphEmailSender : IDeliverySender, IDisposable
{
    #region Fields

    /// <summary>The one scope the sender asks for: the app's Microsoft Graph permissions.</summary>
    private static readonly TokenRequestContext GraphScope = new(["https://graph.microsoft.com/.default"]);

    private static readonly DeliveryFailure NoAnswer = new(IsTransient: true, ReplyCode: string.Empty);

    // Workload identity with no service account token: no attempt can sign in.
    private static readonly DeliveryFailure NoCredential = new(IsTransient: false, ReplyCode: string.Empty);

    private readonly TokenCredential _credential;
    private readonly EmailChannelSettings _email;
    private readonly HttpClient _http;
    private readonly Uri _sendMail;

    #endregion

    #region Constructors

    /// <param name="email">Email settings with the sender <c>Graph</c> and good Graph settings.</param>
    /// <param name="credential">The credential from <see cref="GraphSignIn.Credential" />.</param>
    /// <param name="http">The client the send goes through; it follows no redirect. The sender owns it.</param>
    /// <param name="endpoints">Where to send.</param>
    public GraphEmailSender(
        EmailChannelSettings email,
        TokenCredential credential,
        HttpClient http,
        GraphEndpoints endpoints)
    {
        ArgumentNullException.ThrowIfNull(email);
        ArgumentNullException.ThrowIfNull(endpoints);
        _email = email;
        _credential = credential;
        _http = http;
        // The mailbox comes from the settings only (R4).
        _sendMail = new Uri(endpoints.GraphAddress, $"v1.0/users/{Uri.EscapeDataString(email.Graph.Mailbox)}/sendMail");
    }

    #endregion

    #region Methods

    /// <inheritdoc />
    public void Dispose() => _http.Dispose();

    /// <inheritdoc />
    /// <remarks>
    ///     One time limit covers the token and the send. Only the HTTP status of the failed step is kept, never the
    ///     answer's text: a Graph or Entra ID error text can hold the recipient's address.
    /// </remarks>
    public async Task<DeliveryFailure?> SendAsync(
        Domains.Notifications.Notification notification,
        CancellationToken stoppingToken)
    {
        ArgumentNullException.ThrowIfNull(notification);
        var rendered = notification.RenderedMessage
                       ?? throw new InvalidOperationException("Only a queued notification has a message to send.");

        using var attempt = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
        attempt.CancelAfter(TimeSpan.FromSeconds(_email.TimeoutSeconds));
        try
        {
            var token = await _credential.GetTokenAsync(GraphScope, attempt.Token);
            using var request = new HttpRequestMessage(HttpMethod.Post, _sendMail);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.Token);
            // The design's sendMail body (03-integration): no copy, attachment, sender or Sent Items choice.
            request.Content = JsonContent.Create(new
            {
                message = new
                {
                    subject = rendered.Subject,
                    body = new { contentType = "HTML", content = rendered.Body },
                    toRecipients = new[] { new { emailAddress = new { address = notification.Recipient!.Address } } }
                }
            });
            using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, attempt.Token);
            return response.IsSuccessStatusCode ? null : Answer((int)response.StatusCode, response.Headers);
        }
        catch (Exception error) when (!stoppingToken.IsCancellationRequested && FailureOf(error) is { } failure)
        {
            return failure;
        }
    }

    /// <summary>
    ///     What an error of the sign-in or the send means for the attempt; <see langword="null" /> for an error no
    ///     attempt should meet. The sign-in's errors wrap the Entra ID answer or the network error.
    /// </summary>
    internal static DeliveryFailure? FailureOf(Exception error)
    {
        for (var cause = error; cause is not null; cause = cause.InnerException)
        {
            switch (cause)
            {
                case CredentialUnavailableException:
                    return NoCredential;
                // A throttled sign-in repeats the answer it is throttled after, with no new request.
                case MsalServiceException { StatusCode: > 0 } answer:
                    return Answer(answer.StatusCode, answer.Headers);
                // The attempt's time limit, or a refused or lost connection.
                case OperationCanceledException or HttpRequestException or IOException or SocketException:
                    return NoAnswer;
            }
        }

        return null;
    }

    // 408, 429 and 5xx are transient; any other answer, a redirect among them, is permanent. The Teams sender reuses it.
    internal static DeliveryFailure Answer(int status, HttpResponseHeaders? headers)
    {
        var code = status.ToString(CultureInfo.InvariantCulture);
        return status switch
        {
            429 => new DeliveryFailure(IsTransient: true, code, headers is null ? null : RetryAfterWait.From(headers, DateTimeOffset.UtcNow)),
            408 or >= 500 => new DeliveryFailure(IsTransient: true, code),
            _ => new DeliveryFailure(IsTransient: false, code)
        };
    }

    #endregion
}
