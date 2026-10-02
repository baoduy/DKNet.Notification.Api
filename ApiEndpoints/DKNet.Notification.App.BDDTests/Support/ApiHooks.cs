using Reqnroll.BoDi;

namespace DKNet.Notification.App.BDDTests.Support;

/// <summary>
/// Coordinates one-time API host startup and teardown for the full Reqnroll test run.
/// Do not add additional <c>[BeforeTestRun]</c> hooks in feature step files.
/// </summary>
[Binding]
public sealed class ApiHooks(IObjectContainer objectContainer)
{
    private static BddApiFactory _factory = null!;
    private static HttpClient _client = null!;

    /// <summary>
    /// Boots the shared <see cref="BddApiFactory"/> once per test run and creates a reusable <see cref="HttpClient"/>.
    /// This hook is static because Reqnroll executes <c>[BeforeTestRun]</c> at assembly level.
    /// </summary>
    [BeforeTestRun]
    public static void BeforeTestRun()
    {
        _factory = new BddApiFactory();
        _client = _factory.CreateClient();
    }

    /// <summary>
    /// Disposes the shared <see cref="BddApiFactory"/> once after all scenarios complete.
    /// Keep lifecycle teardown centralized here instead of feature-specific hooks.
    /// </summary>
    [AfterTestRun]
    public static async Task AfterTestRun()
    {
        await _factory.DisposeAsync();
        await RedisServer.StopAsync();
        await MailCatcher.StopSharedAsync();
    }

    /// <summary>
    /// Clears captured logs and registers shared dependencies in Reqnroll's <see cref="IObjectContainer"/>.
    /// Any <c>[Binding]</c> class can inject <see cref="HttpClient"/>, <see cref="ScenarioState"/>, and <see cref="BddApiFactory"/>.
    /// To add a new feature, create <c>Features/&lt;Domain&gt;/&lt;Action&gt;.feature</c> and matching
    /// <c>Features/&lt;Domain&gt;/Steps/&lt;Action&gt;Steps.cs</c>; Reqnroll auto-discovers bindings without csproj edits.
    /// </summary>
    [BeforeScenario(Order = 0)]
    public void BeforeScenario()
    {
        _factory.LogCapture.Clear();
        objectContainer.RegisterInstanceAs<HttpClient>(_client);
        objectContainer.RegisterInstanceAs(_factory);
        objectContainer.RegisterInstanceAs(new ScenarioState());
    }
}
