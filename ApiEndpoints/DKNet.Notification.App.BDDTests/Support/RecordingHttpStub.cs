using System.Collections.Concurrent;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace DKNet.Notification.App.BDDTests.Support;

/// <summary>
/// A local HTTPS stub inside the test, on Kestrel from the ASP.NET shared framework: the Graph stub and the token stub
/// of DRK-2028 §3 "Local run and tests". Its certificate is signed by <see cref="TestCertificateAuthority.Trusted" />,
/// which a test host adds to the Graph sender's trust through the Graph endpoints seam, never through a setting. It
/// records every request it receives, then answers with the next scripted <see cref="Reply" /> that matches the
/// request, or with <see cref="DefaultReply" />. No container and no package: it stands in for Microsoft Graph or
/// Microsoft Entra ID only where a test host points the service at it.
/// </summary>
public sealed class RecordingHttpStub : IAsyncDisposable
{
    private readonly ConcurrentQueue<Request> _requests = new();
    private readonly List<(Func<Request, bool> Matches, Reply Reply)> _scripted = [];
    private readonly Lock _scriptLock = new();
    private WebApplication? _app;
    private int _port;

    private RecordingHttpStub()
    {
    }

    /// <summary>The stub's address, such as <c>https://127.0.0.1:51234/</c>. It keeps its port when paused.</summary>
    public Uri Address { get; private set; } = null!;

    public bool IsRunning => _app is not null;

    /// <summary>Every request the stub received, in order.</summary>
    public IReadOnlyCollection<Request> Requests => _requests.ToArray();

    /// <summary>The answer to every request no scripted reply matches. 202 with no body to begin with.</summary>
    public Reply DefaultReply { get; set; } = Reply.Status(StatusCodes.Status202Accepted);

    public static async Task<RecordingHttpStub> StartAsync()
    {
        var stub = new RecordingHttpStub();
        await stub.RunAsync();
        return stub;
    }

    /// <summary>Answers the first request that <paramref name="matches" /> takes (any, when null) with <paramref name="reply" />, once.</summary>
    public void AnswerOnce(Reply reply, Func<Request, bool>? matches = null)
    {
        lock (_scriptLock)
        {
            _scripted.Add((matches ?? (_ => true), reply));
        }
    }

    /// <summary>Stops listening, so a connection to its port is refused; <see cref="ResumeAsync" /> listens on it again.</summary>
    public async Task PauseAsync() => await StopAsync();

    public async Task ResumeAsync()
    {
        if (_app is null)
        {
            await RunAsync();
        }
    }

    public async Task StopAsync()
    {
        if (_app is null)
        {
            return;
        }

        await _app.StopAsync();
        await _app.DisposeAsync();
        _app = null;
    }

    public async ValueTask DisposeAsync() => await StopAsync();

    private async Task RunAsync()
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.UseKestrelHttpsConfiguration();
        var certificate = TestCertificateAuthority.Trusted.IssueLocalhost();
        builder.WebHost.ConfigureKestrel(kestrel =>
            kestrel.Listen(IPAddress.Loopback, _port, listen => listen.UseHttps(certificate)));
        var app = builder.Build();
        app.Run(AnswerAsync);
        await app.StartAsync();
        // The port the system gave on the first start: the server's own address once it listens.
        _port = new Uri(app.Urls.Single()).Port;
        Address = new Uri($"https://127.0.0.1:{_port}/");
        _app = app;
    }

    private async Task AnswerAsync(HttpContext context)
    {
        using var reader = new StreamReader(context.Request.Body, Encoding.UTF8);
        var request = new Request(
            context.Request.Method,
            context.Request.Path.Value ?? string.Empty,
            context.Request.Headers.ToDictionary(h => h.Key, h => h.Value.ToString(), StringComparer.OrdinalIgnoreCase),
            await reader.ReadToEndAsync(),
            DateTimeOffset.UtcNow);
        _requests.Enqueue(request);
        var reply = NextReply(request);

        if (reply.Delay > TimeSpan.Zero)
        {
            try
            {
                await Task.Delay(reply.Delay, context.RequestAborted);
            }
            catch (OperationCanceledException)
            {
                // The caller gave up first.
                return;
            }
        }

        if (reply.LoseConnection)
        {
            context.Abort();
            return;
        }

        context.Response.StatusCode = reply.StatusCode;
        foreach (var (name, value) in reply.Headers())
        {
            context.Response.Headers[name] = value;
        }

        if (reply.Body is not null)
        {
            context.Response.ContentType = "application/json";
            await context.Response.WriteAsync(reply.Body);
        }
    }

    private Reply NextReply(Request request)
    {
        lock (_scriptLock)
        {
            var index = _scripted.FindIndex(s => s.Matches(request));
            if (index < 0)
            {
                return DefaultReply;
            }

            var reply = _scripted[index].Reply;
            _scripted.RemoveAt(index);
            return reply;
        }
    }

    /// <summary>One request as the stub received it: its path is the unescaped path, with no query.</summary>
    public sealed record Request(
        string Method,
        string Path,
        IReadOnlyDictionary<string, string> Headers,
        string Body,
        DateTimeOffset ReceivedAt)
    {
        /// <summary>Every text the request holds: method, path, header names and values, and body.</summary>
        public string AllText() =>
            string.Join("\n", Headers.SelectMany(h => new[] { h.Key, h.Value }).Prepend(Path).Prepend(Method).Append(Body));
    }

    /// <summary>
    /// One answer: a status with optional headers and a JSON body, given after <see cref="Delay" />; or, with
    /// <see cref="LoseConnection" />, the connection dropped with no answer.
    /// </summary>
    public sealed record Reply(
        int StatusCode,
        string? Body = null,
        Func<IEnumerable<(string Name, string Value)>>? HeadersAtAnswer = null,
        TimeSpan Delay = default,
        bool LoseConnection = false)
    {
        public static Reply Status(int statusCode, string? body = null, params (string Name, string Value)[] headers) =>
            new(statusCode, body, () => headers);

        public static Reply Lost() => new(0, LoseConnection: true);

        /// <summary>A token answer of Microsoft Entra ID, valid for about an hour.</summary>
        public static Reply Token(string accessToken) => Status(
            StatusCodes.Status200OK,
            $$"""{"token_type":"Bearer","expires_in":3599,"ext_expires_in":3599,"access_token":"{{accessToken}}"}""");

        /// <summary>The headers, made when the answer is given (so a date header is counted from then).</summary>
        public IEnumerable<(string Name, string Value)> Headers() => HeadersAtAnswer?.Invoke() ?? [];
    }
}
