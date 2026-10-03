using System.Collections.Concurrent;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace DKNet.Notification.App.BDDTests.Support;

/// <summary>
/// A local HTTP stub inside the test, on Kestrel from the ASP.NET shared framework: the Graph stub and the token stub
/// of DRK-2028 §3 "Local run and tests". It records every request it receives and answers 202 with no body. No
/// container and no package: it stands in for Microsoft Graph or Microsoft Entra ID only where a test host points
/// the service at it.
/// </summary>
public sealed class RecordingHttpStub : IAsyncDisposable
{
    private readonly ConcurrentQueue<Request> _requests = new();
    private WebApplication? _app;

    private RecordingHttpStub()
    {
    }

    /// <summary>The stub's address, such as <c>http://127.0.0.1:51234</c>.</summary>
    public Uri Address { get; private set; } = null!;

    public bool IsRunning => _app is not null;

    /// <summary>Every request the stub received, in order.</summary>
    public IReadOnlyCollection<Request> Requests => _requests.ToArray();

    public static async Task<RecordingHttpStub> StartAsync()
    {
        var stub = new RecordingHttpStub();
        await stub.RunAsync();
        return stub;
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
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        var app = builder.Build();
        app.Run(async context =>
        {
            using var reader = new StreamReader(context.Request.Body, Encoding.UTF8);
            _requests.Enqueue(new Request(
                context.Request.Method,
                context.Request.Path + context.Request.QueryString,
                context.Request.Headers.ToDictionary(h => h.Key, h => h.Value.ToString(), StringComparer.OrdinalIgnoreCase),
                await reader.ReadToEndAsync()));
            context.Response.StatusCode = StatusCodes.Status202Accepted;
        });
        await app.StartAsync();
        // The port the system gave: the server's own address once it listens.
        Address = new Uri(app.Urls.Single());
        _app = app;
    }

    /// <summary>One request as the stub received it.</summary>
    public sealed record Request(string Method, string PathAndQuery, IReadOnlyDictionary<string, string> Headers, string Body);
}
