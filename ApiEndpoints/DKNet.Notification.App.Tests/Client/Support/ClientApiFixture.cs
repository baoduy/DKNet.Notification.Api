using System.Collections.Concurrent;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using DKNet.Notification.App.TestSupport;

namespace DKNet.Notification.App.Tests.Client.Support;

/// <summary>What the API received for one request — observed on the API side, not in the client (DRK-1789 §7).</summary>
public sealed record ReceivedRequest(string Method, string Path, string? Authorization, string? IdempotencyKey);

/// <summary>
/// The in-memory API the client tests call, with the OpenAPI document and docs browser switched on so
/// every address the API serves — including the ones that are not API endpoints — is in its endpoint list.
/// A startup filter records each request the API receives, so credential and idempotency-key checks are
/// made where the API sees them.
/// </summary>
/// <remarks>
/// <c>EnableSwagger</c> is bound by <c>Program.cs</c> before <c>ConfigureAppConfiguration</c> overrides merge
/// in, so it is flipped with the environment variable, the same way <c>AuthOnApiFixture</c> flips
/// <c>RequireAuthorization</c>; safe because this assembly disables test parallelization.
/// </remarks>
public sealed class ClientApiFixture : TestApiFactoryBase, IAsyncLifetime
{
    private const string EnableSwaggerEnvKey = "FeatureManagement__EnableSwagger";

    public ClientApiFixture()
    {
        Environment.SetEnvironmentVariable(EnableSwaggerEnvKey, "true");
    }

    public ConcurrentQueue<ReceivedRequest> ReceivedRequests { get; } = new();

    protected override void ConfigureTestServices(IServiceCollection services)
    {
        base.ConfigureTestServices(services);
        services.AddSingleton<IStartupFilter>(new RecordingStartupFilter(ReceivedRequests));
    }

    public async Task InitializeAsync()
    {
        _ = CreateClient();
        await ResetDatabaseAsync();
    }

    Task IAsyncLifetime.DisposeAsync() => Task.CompletedTask;

    protected override void Dispose(bool disposing)
    {
        Environment.SetEnvironmentVariable(EnableSwaggerEnvKey, null);
        base.Dispose(disposing);
    }

    private sealed class RecordingStartupFilter(ConcurrentQueue<ReceivedRequest> received) : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            app.Use(async (context, nextMiddleware) =>
            {
                var headers = context.Request.Headers;
                received.Enqueue(new ReceivedRequest(
                    context.Request.Method,
                    context.Request.Path.Value ?? string.Empty,
                    headers.Authorization.Count == 0 ? null : headers.Authorization.ToString(),
                    headers.TryGetValue("X-Idempotency-Key", out var key) ? key.ToString() : null));
                await nextMiddleware(context);
            });
            next(app);
        };
    }
}
