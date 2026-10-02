using System.Collections.Concurrent;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;

namespace DKNet.Notification.App.BDDTests.Support;

/// <summary>
/// A small SMTP server inside the test, for the one reply Mailpit cannot give: a reply text that holds the
/// recipient's address (DRK-2020 §5 "An SMTP reply text never reaches the logs"). It speaks STARTTLS only, on a
/// certificate signed by <see cref="TestCertificateAuthority.Trusted" />, and answers each recipient with the reply
/// <c>recipientReply</c> gives, or accepts it.
/// </summary>
public sealed class ScriptedSmtpServer : IAsyncDisposable
{
    private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
    private readonly X509Certificate2 _certificate = TestCertificateAuthority.Trusted.IssueLocalhost();
    private readonly Func<string, string?> _recipientReply;
    private readonly CancellationTokenSource _stop = new();
    private readonly ConcurrentQueue<string> _recipients = new();
    private readonly ConcurrentQueue<string> _delivered = new();
    private readonly Task _accepting;

    /// <param name="recipientReply">The full reply line to a recipient's address; null accepts it.</param>
    public ScriptedSmtpServer(Func<string, string?> recipientReply)
    {
        _recipientReply = recipientReply;
        _listener.Start();
        _accepting = AcceptAsync();
    }

    /// <summary>The host name the service reaches the server on; its certificate names it.</summary>
    public string Host => "localhost";

    public int Port => ((IPEndPoint)_listener.LocalEndpoint).Port;

    /// <summary>Every recipient the service named, in order.</summary>
    public IReadOnlyCollection<string> Recipients => _recipients.ToArray();

    /// <summary>The recipient of every mail the server took, in order.</summary>
    public IReadOnlyCollection<string> Delivered => _delivered.ToArray();

    /// <summary>The settings of a service with email set up to send to this server over STARTTLS, without sign-in.</summary>
    public IReadOnlyDictionary<string, string?> EmailSettings() => new Dictionary<string, string?>(StringComparer.Ordinal)
    {
        ["Notifications:Email:Enabled"] = "true",
        ["Notifications:Email:Sender"] = "Smtp",
        ["Notifications:Email:TimeoutSeconds"] = "30",
        ["Notifications:Email:Smtp:Host"] = Host,
        ["Notifications:Email:Smtp:Port"] = Port.ToString(System.Globalization.CultureInfo.InvariantCulture),
        ["Notifications:Email:Smtp:Security"] = "StartTls",
        ["Notifications:Email:Smtp:FromAddress"] = "notifications@drunkcoding.net",
        ["Notifications:Email:Smtp:FromName"] = "DKNet Notification"
    };

    public async ValueTask DisposeAsync()
    {
        await _stop.CancelAsync();
        _listener.Stop();
        try
        {
            await _accepting;
        }
        catch (Exception e) when (e is OperationCanceledException or SocketException or ObjectDisposedException)
        {
        }

        _listener.Dispose();
        _certificate.Dispose();
        _stop.Dispose();
    }

    private async Task AcceptAsync()
    {
        while (!_stop.IsCancellationRequested)
        {
            var client = await _listener.AcceptTcpClientAsync(_stop.Token);
            _ = Task.Run(() => ServeAsync(client));
        }
    }

    private async Task ServeAsync(TcpClient client)
    {
        using var _ = client;
        Stream stream = client.GetStream();
        var reader = new StreamReader(stream, Encoding.ASCII, leaveOpen: true);
        var secure = false;
        var accepted = new List<string>();
        try
        {
            await WriteAsync(stream, "220 scripted ESMTP ready");
            while (await reader.ReadLineAsync(_stop.Token) is { } line)
            {
                var command = line.Length >= 4 ? line[..4].ToUpperInvariant() : line.ToUpperInvariant();
                switch (command)
                {
                    case "EHLO":
                        await WriteAsync(stream, secure ? "250-scripted\r\n250 8BITMIME" : "250-scripted\r\n250-8BITMIME\r\n250 STARTTLS");
                        break;
                    case "HELO":
                        await WriteAsync(stream, "250 scripted");
                        break;
                    case "STAR" when !secure:
                        await WriteAsync(stream, "220 2.0.0 Ready to start TLS");
                        var tls = new SslStream(stream);
                        await tls.AuthenticateAsServerAsync(_certificate);
                        stream = tls;
                        reader = new StreamReader(stream, Encoding.ASCII, leaveOpen: true);
                        secure = true;
                        break;
                    case "MAIL" when !secure:
                    case "RCPT" when !secure:
                    case "DATA" when !secure:
                        await WriteAsync(stream, "530 5.7.0 Must issue a STARTTLS command first");
                        break;
                    case "MAIL":
                        accepted.Clear();
                        await WriteAsync(stream, "250 2.1.0 Ok");
                        break;
                    case "RCPT":
                        var recipient = line[(line.IndexOf('<') + 1)..line.IndexOf('>')];
                        _recipients.Enqueue(recipient);
                        var reply = _recipientReply(recipient);
                        if (reply is null)
                        {
                            accepted.Add(recipient);
                        }

                        await WriteAsync(stream, reply ?? "250 2.1.5 Ok");
                        break;
                    case "DATA" when accepted.Count == 0:
                        await WriteAsync(stream, "554 5.5.1 No valid recipients");
                        break;
                    case "DATA":
                        await WriteAsync(stream, "354 End data with <CR><LF>.<CR><LF>");
                        while (await reader.ReadLineAsync(_stop.Token) is { } data && data != ".")
                        {
                        }

                        foreach (var to in accepted)
                        {
                            _delivered.Enqueue(to);
                        }

                        accepted.Clear();
                        await WriteAsync(stream, "250 2.0.0 Ok: queued");
                        break;
                    case "RSET":
                        accepted.Clear();
                        await WriteAsync(stream, "250 2.0.0 Ok");
                        break;
                    case "NOOP":
                        await WriteAsync(stream, "250 2.0.0 Ok");
                        break;
                    case "QUIT":
                        await WriteAsync(stream, "221 2.0.0 Bye");
                        return;
                    default:
                        await WriteAsync(stream, "502 5.5.2 Command not recognized");
                        break;
                }
            }
        }
        catch (Exception e) when (e is IOException or OperationCanceledException or ObjectDisposedException or System.Security.Authentication.AuthenticationException)
        {
            // The service closed the connection, or the run is over.
        }
    }

    private async Task WriteAsync(Stream stream, string reply)
    {
        await stream.WriteAsync(Encoding.ASCII.GetBytes(reply + "\r\n"), _stop.Token);
        await stream.FlushAsync(_stop.Token);
    }
}
