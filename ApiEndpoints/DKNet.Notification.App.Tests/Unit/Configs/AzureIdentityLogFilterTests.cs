using System.Collections.Concurrent;
using System.Diagnostics.Tracing;
using System.Text;
using DKNet.Notification.Api.Configs;
using DKNet.Notification.AppServices.Delivery;
using DKNet.Notification.Share.Options;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DKNet.Notification.App.Tests.Unit.Configs;

/// <summary>
/// DRK-2028 R2 (brief DRK-2030, rework item 2): the Azure Monitor set-up forwards the Azure SDK's EventSources to the
/// logs, the <c>Azure-Identity</c> one as the category <c>Azure.Identity</c>, whose entries can hold Microsoft Entra
/// ID's error text. The host is the release's log set-up with an Azure Monitor connection string that points at a
/// port where nothing listens; the token request fails at a fake Entra ID that answers with an error text.
/// </summary>
[Collection(SerialTestsCollection.Name)]
public sealed class AzureIdentityLogFilterTests
{
    [Fact]
    public async Task No_Azure_Identity_entry_reaches_a_log_provider_when_Azure_Monitor_forwards_the_Azure_SDK_logs()
    {
        var builder = WebApplication.CreateBuilder();
        builder.Configuration["AzureMonitor:ConnectionString"] =
            "InstrumentationKey=00000000-0000-0000-0000-000000000000;IngestionEndpoint=https://127.0.0.1:9/";
        builder.AddLogConfig(new FeatureOptions { EnableOpenTelemetry = true });
        var logs = new CapturingLoggerProvider();
        builder.Logging.AddProvider(logs);
        await using var app = builder.Build();
        await app.StartAsync();

        await Should.ThrowAsync<Azure.Identity.AuthenticationFailedException>(() => FailingSignIn()
            .GetTokenAsync(new Azure.Core.TokenRequestContext(["https://graph.microsoft.com/.default"]), CancellationToken.None)
            .AsTask());
        // The forwarding is live in this host: an Azure SDK EventSource of any other name reaches the logs.
        using (var probe = new AzureProbeEventSource())
        {
            probe.Probe("probe");
        }

        await app.StopAsync();
        logs.Categories.ShouldContain("Azure.Probe");
        logs.Categories.ShouldNotContain("Azure.Identity");
        logs.Texts.ShouldAllBe(text => !text.Contains("7f3a-9921", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void The_Azure_Identity_category_is_off_with_or_without_OpenTelemetry(bool openTelemetry)
    {
        var builder = WebApplication.CreateBuilder();
        builder.AddLogConfig(new FeatureOptions { EnableOpenTelemetry = openTelemetry });
        using var services = builder.Services.BuildServiceProvider();

        services.GetRequiredService<IOptions<LoggerFilterOptions>>().Value.Rules
            .ShouldContain(rule => rule.CategoryName == "Azure.Identity" && rule.LogLevel == LogLevel.None && rule.ProviderName == null);
    }

    private static Azure.Core.TokenCredential FailingSignIn() => GraphSignIn.Credential(
        new GraphSenderSettings
        {
            TenantId = "3f2b9c1e-6a4d-4e0b-9d57-1c2f8a7e5b10",
            ClientId = "7c1d4e2a-0b9f-4a63-8e15-2d6f9b3c8a41",
            Credential = "ClientSecret",
            ClientSecret = "Gr4ph-s3cret-9921",
            Mailbox = "notify@contoso.com"
        },
        new GraphEndpoints(new Uri("https://graph.test"), new Uri($"https://login-{Guid.NewGuid():N}.test/"), [], serviceAccountTokenFile: null),
        new EntraIdWithErrorText());

    /// <summary>An Azure SDK EventSource of another name, as the Azure SDK marks its own.</summary>
    [EventSource(Name = "Azure-Probe")]
    private sealed class AzureProbeEventSource() : EventSource(EventSourceSettings.Default, "AzureEventSource", "true")
    {
        [Event(1, Level = EventLevel.Warning, Message = "{0}")]
        public void Probe(string message) => WriteEvent(1, message);
    }

    /// <summary>Answers every token request with a 400 whose description is Entra ID's error text.</summary>
    private sealed class EntraIdWithErrorText : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.BadRequest)
            {
                Content = new StringContent(
                    """{"error":"invalid_request","error_description":"Bad request, trace 7f3a-9921"}""",
                    Encoding.UTF8,
                    "application/json")
            });
    }

    private sealed class CapturingLoggerProvider : ILoggerProvider
    {
        private readonly ConcurrentQueue<(string Category, string Text)> _entries = new();

        public IReadOnlyCollection<string> Categories => _entries.Select(e => e.Category).Distinct(StringComparer.Ordinal).ToArray();

        public IReadOnlyCollection<string> Texts => _entries.Select(e => e.Text).ToArray();

        public ILogger CreateLogger(string categoryName) => new CapturingLogger(categoryName, _entries);

        public void Dispose()
        {
        }

        private sealed class CapturingLogger(string category, ConcurrentQueue<(string, string)> entries) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state)
                where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
                entries.Enqueue((category, $"{formatter(state, exception)} {exception}"));
        }
    }
}
