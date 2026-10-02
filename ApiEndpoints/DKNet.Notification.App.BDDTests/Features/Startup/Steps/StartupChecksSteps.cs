using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using DKNet.AspCore.Idempotency.Store;

namespace DKNet.Notification.App.BDDTests.Features.Startup.Steps;

/// <summary>
/// Steps for <c>StartupChecks.feature</c> (DRK-2013 §5). Every expected value is a literal from the spec.
/// </summary>
/// <remarks>
/// Scoped to this feature: surface B binds "the service runs with its released template catalogue and sign-in on"
/// for its own host, and Reqnroll picks the scoped binding over an unscoped one with the same text, so the two
/// never clash. Settings go in through <c>UseSetting</c>, which the host reads before <c>Build()</c>, ahead of
/// the settings files, user secrets and environment variables.
/// </remarks>
[Binding]
[Scope(Feature = "Send API start-up checks and a quiet health check")]
public sealed class StartupChecksSteps : IDisposable
{
    /// <summary>A setting value that must never reach a start-up error.</summary>
    private const string SentinelSettingValue = "drk-2013-sentinel-setting-value";

    private const string HealthyStatusOnly = """{"status":"Healthy"}""";

    private string? _environment;
    private WebApplicationFactory<DKNet.Notification.Api.Program>? _factory;
    private Exception? _startError;
    private SignInOnApiFactory? _signInOnFactory;
    private readonly List<(HttpStatusCode Status, string Body)> _healthAnswers = [];
    private IReadOnlyCollection<string> _probeLogs = [];

    #region Given

    [Given(@"^the service is set to run as ""(.*)"" with no Redis connection$")]
    public void GivenTheServiceIsSetToRunAsWithNoRedisConnection(string environment) => _environment = environment;

    [Given("the service runs with its released template catalogue and sign-in on")]
    public async Task GivenTheServiceRunsWithItsReleasedTemplateCatalogueAndSignInOn()
    {
        _signInOnFactory = new SignInOnApiFactory();
        using var client = _signInOnFactory.CreateClient();

        // Sign-in is on: any route but the health check needs a token.
        using var answer = await client.GetAsync("/");
        answer.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    #endregion

    #region When

    [When("the service starts")]
    public void WhenTheServiceStarts()
    {
        _environment.ShouldNotBeNull("a Given step must choose the environment first");
        _factory = new WebApplicationFactory<DKNet.Notification.Api.Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment(_environment);
            builder.UseSetting("ConnectionStrings:Redis", string.Empty);
            builder.UseSetting("ConnectionStrings:Sentinel", SentinelSettingValue);
        });

        try
        {
            _ = _factory.Services;
        }
        catch (Exception error)
        {
            _startError = error;
        }
    }

    [When("an operator's monitor checks the health of the service 3 times, without a token")]
    public async Task WhenAnOperatorsMonitorChecksTheHealthOfTheService3TimesWithoutAToken()
    {
        _signInOnFactory.ShouldNotBeNull();
        using var client = _signInOnFactory.CreateClient();

        // The start-up wrote entries, so the capture is attached; the probes start from an empty capture.
        _signInOnFactory.LogCapture.Messages.ShouldNotBeEmpty();
        _signInOnFactory.LogCapture.Clear();

        for (var probe = 0; probe < 3; probe++)
        {
            using var answer = await client.GetAsync("/healthz");
            _healthAnswers.Add((answer.StatusCode, await answer.Content.ReadAsStringAsync()));
        }

        _probeLogs = _signInOnFactory.LogCapture.Messages;
    }

    #endregion

    #region Then

    [Then("the service refuses to start and names the missing Redis connection")]
    public void ThenTheServiceRefusesToStartAndNamesTheMissingRedisConnection()
    {
        _startError.ShouldNotBeNull("the service started without a Redis connection");
        var messages = Chain(_startError).Select(e => e.Message).ToList();
        messages.ShouldContain(m => m.Contains("ConnectionStrings:Redis", StringComparison.Ordinal),
            $"no start-up error names the missing setting: {string.Join(" | ", messages)}");
        messages.ShouldAllBe(m => !m.Contains(SentinelSettingValue, StringComparison.Ordinal));
    }

    [Then("the service starts and keeps idempotency records in memory")]
    public void ThenTheServiceStartsAndKeepsIdempotencyRecordsInMemory()
    {
        _startError.ShouldBeNull();
        _factory!.Services.GetRequiredService<IIdempotencyKeyStore>().GetType().FullName
            .ShouldBe("DKNet.AspCore.Idempotency.Store.IdempotencyInMemoryStore");
    }

    [Then("each check answers healthy, with only a status")]
    public void ThenEachCheckAnswersHealthyWithOnlyAStatus()
    {
        _healthAnswers.Count.ShouldBe(3);
        _healthAnswers.ShouldAllBe(a => a.Status == HttpStatusCode.OK && a.Body == HealthyStatusOnly);
    }

    [Then("the health check writes no log entry")]
    public void ThenTheHealthCheckWritesNoLogEntry() =>
        _probeLogs.ShouldBeEmpty($"the probes wrote: {string.Join(" | ", _probeLogs)}");

    #endregion

    private static IEnumerable<Exception> Chain(Exception error)
    {
        for (var current = error; current is not null; current = current.InnerException)
        {
            yield return current;
        }
    }

    public void Dispose()
    {
        _factory?.Dispose();
        _signInOnFactory?.Dispose();
    }

    /// <summary>The Testing host with sign-in on and the real bearer scheme: no caller holds a token.</summary>
    private sealed class SignInOnApiFactory : TestApiFactoryBase
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.UseSetting("FeatureManagement:RequireAuthorization", "true");
        }
    }
}
