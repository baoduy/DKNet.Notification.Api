using System.Globalization;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;
using MailKit;
using MailKit.Net.Smtp;
using MailKit.Security;
using MimeKit;
using MimeKit.Text;

namespace DKNet.Notification.AppServices.Delivery;

/// <summary>
///     The SMTP sender (ADR-0005): one connection per attempt, over STARTTLS or TLS as <see cref="SmtpSenderSettings.Security" />
///     says, with the server certificate always checked, signed in only when a user name is set.
/// </summary>
public sealed class SmtpEmailSender(EmailChannelSettings email, SmtpTrustedRoots trustedRoots) : IDeliverySender
{
    #region Fields

    private static readonly DeliveryFailure NoReply = new(IsTransient: true, ReplyCode: string.Empty);

    private static readonly DeliveryFailure RefusedSignIn = new(IsTransient: false, ReplyCode: string.Empty);

    // No attempt can send a mail its addresses cannot be written in.
    private static readonly DeliveryFailure Unsendable = new(IsTransient: false, ReplyCode: string.Empty);

    #endregion

    #region Methods

    /// <inheritdoc />
    /// <remarks>
    ///     Only the reply code of a MailKit error is kept: its message holds the reply text, which can hold the
    ///     recipient's address.
    /// </remarks>
    public async Task<DeliveryFailure?> SendAsync(
        Domains.Notifications.Notification notification,
        CancellationToken stoppingToken)
    {
        ArgumentNullException.ThrowIfNull(notification);
        using var message = Message(notification);
        if (message is null)
        {
            return Unsendable;
        }

        using var attempt = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
        attempt.CancelAfter(TimeSpan.FromSeconds(email.TimeoutSeconds));

        using var client = new SmtpClient();
        if (trustedRoots.Certificates.Count > 0)
        {
            client.ServerCertificateValidationCallback = (_, certificate, _, errors) =>
                IsTrusted(certificate, errors, trustedRoots.Certificates);
        }

        var smtp = email.Smtp;
        try
        {
            await client.ConnectAsync(smtp.Host, smtp.Port, Security(smtp.Security), attempt.Token);
            if (smtp.UserName.Length > 0)
            {
                await client.AuthenticateAsync(smtp.UserName, smtp.Password, attempt.Token);
            }

            await client.SendAsync(message, attempt.Token);
        }
        catch (OperationCanceledException) when (!stoppingToken.IsCancellationRequested)
        {
            // The attempt's time limit passed: a timeout.
            return NoReply;
        }
        catch (Exception error) when (FailureOf(error) is { } failure)
        {
            return failure;
        }

        // The provider accepted the mail: a failed goodbye does not undo the delivery.
        await QuitAsync(client);
        return null;
    }

    /// <returns>
    ///     The mail; <see langword="null" /> when an address the recipient rule takes cannot be written in a mail
    ///     header, such as <c>jane.@example.com</c>.
    /// </returns>
    private MimeMessage? Message(Domains.Notifications.Notification notification)
    {
        var rendered = notification.RenderedMessage
                       ?? throw new InvalidOperationException("Only a queued notification has a message to send.");
        MailboxAddress from;
        MailboxAddress to;
        try
        {
            from = new MailboxAddress(email.Smtp.FromName, email.Smtp.FromAddress);
            to = new MailboxAddress(string.Empty, notification.Recipient!.Address);
        }
        catch (ParseException)
        {
            return null;
        }

        var message = new MimeMessage
        {
            Subject = rendered.Subject,
            Body = new TextPart(TextFormat.Html) { Text = rendered.Body }
        };
        message.From.Add(from);
        message.To.Add(to);
        return message;
    }

    private static SecureSocketOptions Security(string security) =>
        string.Equals(security, "Tls", StringComparison.OrdinalIgnoreCase)
            ? SecureSocketOptions.SslOnConnect
            : SecureSocketOptions.StartTls;

    /// <summary>What a MailKit error means for the attempt; <see langword="null" /> for an error no attempt should meet.</summary>
    private static DeliveryFailure? FailureOf(Exception error) => error switch
    {
        SmtpCommandException reply => Reply(reply.StatusCode),
        // A refused sign-in: MailKit keeps the server's reply as the inner error.
        AuthenticationException { InnerException: SmtpCommandException reply } => Reply(reply.StatusCode),
        AuthenticationException => RefusedSignIn,
        // A lost or refused connection, a failed TLS handshake or a certificate that does not check out.
        IOException or SocketException or SslHandshakeException or ProtocolException or TimeoutException or SaslException
            or NotSupportedException => NoReply,
        _ => null
    };

    // 4xx is transient; 5xx, a refused sign-in (535) among them, is permanent.
    private static DeliveryFailure Reply(SmtpStatusCode code) =>
        new((int)code is >= 400 and < 500, ((int)code).ToString(CultureInfo.InvariantCulture));

    private static async Task QuitAsync(SmtpClient client)
    {
        try
        {
            using var quit = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            await client.DisconnectAsync(quit: true, quit.Token);
        }
        catch (Exception error) when (error is IOException or SocketException or ProtocolException or CommandException
                                          or OperationCanceledException)
        {
            // The connection closes with the client.
        }
    }

    /// <summary>
    ///     The machine's own check first; a chain that fails it is built again on the extra authorities only. A name
    ///     mismatch, or a certificate the server did not send, is never accepted.
    /// </summary>
    internal static bool IsTrusted(
        X509Certificate? certificate,
        SslPolicyErrors errors,
        IReadOnlyCollection<X509Certificate2> extraRoots)
    {
        if (errors == SslPolicyErrors.None)
        {
            return true;
        }

        if (errors != SslPolicyErrors.RemoteCertificateChainErrors || certificate is null)
        {
            return false;
        }

        using var extraChain = new X509Chain();
        extraChain.ChainPolicy.TrustMode = X509ChainTrustMode.CustomRootTrust;
        extraChain.ChainPolicy.CustomTrustStore.AddRange(extraRoots.ToArray());
        // The extra authorities are test authorities: they publish no revocation list.
        extraChain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
        using var serverCertificate = new X509Certificate2(certificate);
        return extraChain.Build(serverCertificate);
    }

    #endregion
}
