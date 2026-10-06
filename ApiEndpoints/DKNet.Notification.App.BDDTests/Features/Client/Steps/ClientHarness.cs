using System.Collections.Concurrent;
using DKNet.Notification.App.BDDTests.Features.Notifications.Steps;
using DKNet.Notification.Client;
using Microsoft.Extensions.Http;
using Microsoft.Extensions.Logging;

namespace DKNet.Notification.App.BDDTests.Features.Client.Steps;

/// <summary>
/// The state of one client scenario: the caller's own service container with the client registered in it, the
/// caller's token handler, every answer the client gave and the ids a step bound to a quoted name. Wraps the send
/// scenario's host (<see cref="SendScenario" />) for the @integration scenarios.
/// </summary>
public sealed class ClientScenario(SendScenario service) : IAsyncDisposable
{
    /// <summary>The address the @unit scenarios register; nothing listens there, the stub answers.</summary>
    public static readonly Uri StubAddress = new("https://notifications.example.test/");

    private ServiceProvider? _provider;
    private INotificationClient? _client;

    public SendScenario Service => service;

    public bool ServiceStarted { get; set; }

    /// <summary>
/// Puts the scenario's transport under every handler chain of the caller's container. A filter wraps whatever the
/// registration configured, so it sets the primary handler last, however the client registers its own.
/// </summary>
public sealed class PrimaryHandlerFilter(Func<HttpMessageHandler> primaryHandler) : IHttpMessageHandlerBuilderFilter
{
    public Action<HttpMessageHandlerBuilder> Configure(Action<HttpMessageHandlerBuilder> next) =>
        builder =>
        {
            next(builder);
            builder.PrimaryHandler = primaryHandler();
        };
}

/// <summary>What the caller's token handler adds, and how often it ran.</summary>
    public TokenHandlerState Token { get; } = new();

    /// <summary>Every entry the caller's container logged, at Trace and above.</summary>
    public TestLogCapture Logs { get; } = new();

    /// <summary>A quoted notification id of the spec, bound to the id that call really got.</summary>
    public Dictionary<string, Guid> Ids { get; } = new(StringComparer.Ordinal);

    public INotificationClient Client => _client.ShouldNotBeNull("a Given step must register the client first");

    /// <summary>The last request the client was asked to send.</summary>
    public SendNotificationRequest? LastRequest { get; private set; }

    public SendNotificationResponse? LastSend { get; private set; }

    public NotificationStatusResponse? LastStatus { get; private set; }

    /// <summary>The exception the last call raised; null when it returned.</summary>
    public Exception? Raised { get; private set; }

    /// <summary>
    /// Registers the client the way a caller does, in a container of its own, with
    /// <paramref name="primaryHandler" /> as the transport under every handler the registration adds.
    /// </summary>
    /// <param name="withTokenHandler">Registers through the overload that takes the caller's token handler.</param>
    public void Register(Uri baseAddress, Func<HttpMessageHandler> primaryHandler, bool withTokenHandler)
    {
        _provider?.Dispose();
        var services = new ServiceCollection();
        services.AddLogging(logging => logging.SetMinimumLevel(LogLevel.Trace).AddProvider(Logs));
        services.AddSingleton(Token);
        services.AddTransient<CallerTokenHandler>();
        services.AddSingleton<IHttpMessageHandlerBuilderFilter>(new PrimaryHandlerFilter(primaryHandler));
        if (withTokenHandler)
        {
            services.AddNotificationClient(baseAddress, typeof(CallerTokenHandler));
        }
        else
        {
            services.AddNotificationClient(baseAddress);
        }

        _provider = services.BuildServiceProvider();
        _client = _provider.GetRequiredService<INotificationClient>();
    }

    public async Task SendAsync(SendNotificationRequest request, string idempotencyKey)
    {
        LastRequest = request;
        LastSend = await CallAsync(client => client.SendAsync(request, idempotencyKey));
    }

    public async Task ReadStatusAsync(Guid notificationId) =>
        LastStatus = await CallAsync(client => client.GetStatusAsync(notificationId));

    /// <summary>The id a quoted spec id stands for: the bound one, else the quoted value itself.</summary>
    public Guid Id(string quoted) => Ids.TryGetValue(quoted, out var bound) ? bound : Guid.Parse(quoted);

    /// <summary>The refusal the last call raised, as the client's own exception type.</summary>
    public NotificationApiException Refusal() =>
        Raised.ShouldNotBeNull("the call must be refused").ShouldBeOfType<NotificationApiException>();

    /// <summary>The last call returned; shows what it raised otherwise.</summary>
    public void ShouldHaveReturned() => Raised.ShouldBeNull(Raised?.ToString());

    public async ValueTask DisposeAsync()
    {
        if (_provider is not null)
        {
            await _provider.DisposeAsync();
        }
    }

    private async Task<T?> CallAsync<T>(Func<INotificationClient, Task<T>> call)
        where T : class
    {
        Raised = null;
        try
        {
            return await call(Client);
        }
        catch (Exception exception)
        {
            Raised = exception;
            return null;
        }
    }
}

/// <summary>
/// Puts the scenario's transport under every handler chain of the caller's container. A filter wraps whatever the
/// registration configured, so it sets the primary handler last, however the client registers its own.
/// </summary>
public sealed class PrimaryHandlerFilter(Func<HttpMessageHandler> primaryHandler) : IHttpMessageHandlerBuilderFilter
{
    public Action<HttpMessageHandlerBuilder> Configure(Action<HttpMessageHandlerBuilder> next) =>
        builder =>
        {
            next(builder);
            builder.PrimaryHandler = primaryHandler();
        };
}

/// <summary>What the caller's token handler adds to each request, and what it saw come back.</summary>
public sealed class TokenHandlerState
{
    private int _runs;

    /// <summary>The <c>Authorization</c> value the handler adds.</summary>
    public string Authorization { get; set; } = SendScenario.SharedToken;

    /// <summary>The claims the test sign-in reads (<see cref="TestAuthHandler.ClaimsHeader" />); null adds none.</summary>
    public string? Claims { get; set; }

    /// <summary>False once the handler stops adding a token.</summary>
    public bool Adding { get; set; } = true;

    public int Runs => _runs;

    /// <summary>Every answer that passed back through the handler, in order.</summary>
    public ConcurrentQueue<WireAnswer> Answers { get; } = new();

    internal void Ran() => Interlocked.Increment(ref _runs);
}

/// <summary>One answer as it came over the wire.</summary>
public sealed record WireAnswer(HttpMethod Method, HttpStatusCode Status, string Body);

/// <summary>
/// The caller's own token handler, as a caller writes one: it adds the bearer token (and, for the test sign-in, the
/// claims of that token). It also keeps each answer's status and body, so a check can compare the client's
/// exception with the bytes the service sent.
/// </summary>
public sealed class CallerTokenHandler(TokenHandlerState state) : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        state.Ran();
        if (state.Adding)
        {
            request.Headers.TryAddWithoutValidation("Authorization", state.Authorization).ShouldBeTrue();
            if (state.Claims is not null)
            {
                request.Headers.TryAddWithoutValidation(TestAuthHandler.ClaimsHeader, state.Claims).ShouldBeTrue();
            }
        }

        var response = await base.SendAsync(request, cancellationToken);
        // Buffered here, so the client still reads the whole body afterwards.
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        state.Answers.Enqueue(new WireAnswer(request.Method, response.StatusCode, body));
        return response;
    }
}

/// <summary>
/// The service for the @unit scenarios: answers each request from a script and keeps what it received, so a check
/// can count the sends and read the credential each one carried.
/// </summary>
public sealed class ScriptedService(Func<HttpRequestMessage, HttpResponseMessage> answer) : HttpMessageHandler
{
    private readonly ConcurrentQueue<ReceivedRequest> _received = new();

    public IReadOnlyList<ReceivedRequest> Received => _received.ToArray();

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        _received.Enqueue(new ReceivedRequest(
            request.Method,
            request.RequestUri.ShouldNotBeNull().AbsolutePath,
            request.Headers.TryGetValues("Authorization", out var values) ? string.Join(", ", values) : null));
        return Task.FromResult(answer(request));
    }

    public sealed record ReceivedRequest(HttpMethod Method, string Path, string? Authorization);
}
