using System.Diagnostics;
using System.IO.Compression;
using System.Xml.Linq;

namespace DKNet.Notification.App.BDDTests.Features.Client.Steps;

/// <summary>
/// Steps for the route parity, package and dependency scenarios of <c>NotificationClient.feature</c> (DRK-2141 §5).
/// Every expected value is a literal from the spec or the brief (DRK-2145 §3 rows 1 and 8).
/// </summary>
[Binding]
[Scope(Feature = NotificationClientSteps.FeatureTitle)]
public sealed class ClientPackageSteps(ClientScenario client)
{
    private const string RepoUrl = "https://github.com/baoduy/DKNet.Notification.Api";
    private const string ClientProject = "ApiEndpoints/DKNet.Notification.Client/DKNet.Notification.Client.csproj";

    private static readonly string[] ServiceProjects =
    [
        "ApiEndpoints/DKNet.Notification.Api/DKNet.Notification.Api.csproj",
        "ApiEndpoints/DKNet.Notification.AppServices/DKNet.Notification.AppServices.csproj",
        "ApiEndpoints/DKNet.Notification.Domains/DKNet.Notification.Domains.csproj",
        "ApiEndpoints/DKNet.Notification.Share/DKNet.Notification.Share.csproj"
    ];

    private IReadOnlyList<LiveRoute> _routes = [];
    private IReadOnlyList<ClientOperation> _operations = [];
    private string? _packageDirectory;
    private XElement? _nuspec;
    private string? _readme;
    private IReadOnlyList<(string Project, string Kind, string Include)> _references = [];

    [AfterScenario]
    public void AfterScenario()
    {
        if (_packageDirectory is not null && Directory.Exists(_packageDirectory))
        {
            Directory.Delete(_packageDirectory, recursive: true);
        }
    }

    #region Route parity

    [Given(@"^the notification service's live caller routes$")]
    public async Task GivenTheNotificationServicesLiveCallerRoutes()
    {
        await client.Service.StartAsync(signIn: true, withRedis: false);
        client.ServiceStarted = true;
        _routes = EndpointParity.LiveRoutes(client.Service.Factory.Services);
        EndpointParity.CallerRoutes(_routes).ShouldNotBeEmpty();
    }

    [When(@"^the route parity check compares them with the client's operations$")]
    public void WhenTheRouteParityCheckComparesThemWithTheClientsOperations() =>
        _operations = EndpointParity.ClientOperations();

    [Then(@"^each live caller route matches exactly 1 client operation$")]
    public void ThenEachLiveCallerRouteMatchesExactly1ClientOperation()
    {
        var drift = EndpointParity.CallerRoutes(_routes)
            .Select(r => (Route: r.ToString(), Operations: EndpointParity.OperationsFor(r, _operations)))
            .Where(m => m.Operations.Count != 1)
            .Select(m => $"{m.Route} -> [{string.Join(", ", m.Operations)}]")
            .ToList();
        drift.ShouldBeEmpty();
    }

    [Then(@"^each client operation matches exactly 1 live caller route$")]
    public void ThenEachClientOperationMatchesExactly1LiveCallerRoute()
    {
        _operations.ShouldNotBeEmpty();
        var drift = _operations
            .Select(o => (o.Name, Routes: EndpointParity.RoutesFor(o, EndpointParity.CallerRoutes(_routes))))
            .Where(m => m.Routes.Count != 1)
            .Select(m => $"{m.Name} -> [{string.Join(", ", m.Routes)}]")
            .ToList();
        drift.ShouldBeEmpty();
    }

    [Then(@"^the health route has no client operation$")]
    public void ThenTheHealthRouteHasNoClientOperation()
    {
        var health = _routes.Where(r => r.Path == EndpointParity.HealthRoute).ShouldHaveSingleItem();
        EndpointParity.OperationsFor(health, _operations).ShouldBeEmpty();
    }

    #endregion

    #region Package

    [Given(@"^the client package is packed$")]
    public async Task GivenTheClientPackageIsPacked()
    {
        _packageDirectory = Directory.CreateTempSubdirectory("notification-client-").FullName;
        var (exitCode, output) = await DotnetAsync(
            "pack", Path.Combine(RepoRoot(), ClientProject), "-c", "Release", "-o", _packageDirectory, "--nologo");
        exitCode.ShouldBe(0, output);
    }

    [When(@"^a developer opens the package$")]
    public void WhenADeveloperOpensThePackage()
    {
        var package = Directory.GetFiles(_packageDirectory.ShouldNotBeNull(), "DKNet.Notification.Client.*.nupkg")
            .ShouldHaveSingleItem("the pack must leave 1 package");
        using var zip = ZipFile.OpenRead(package);
        _nuspec = XDocument.Load(zip.GetEntry("DKNet.Notification.Client.nuspec").ShouldNotBeNull().Open())
            .Root.ShouldNotBeNull();
        var readme = zip.GetEntry("README.md");
        if (readme is not null)
        {
            using var reader = new StreamReader(readme.Open());
            _readme = reader.ReadToEnd();
        }
    }

    [Then(@"^it carries a README with install, register, send and read-status steps$")]
    public void ThenItCarriesAReadmeWithInstallRegisterSendAndReadStatusSteps()
    {
        Metadata("readme").ShouldBe("README.md");
        var headings = _readme.ShouldNotBeNull("the package must carry README.md at its root")
            .Split('\n')
            .Select(line => line.TrimEnd('\r'))
            .Where(line => line.StartsWith("## ", StringComparison.Ordinal))
            .ToList();
        headings.ShouldBe(["## Install", "## Register", "## Send", "## Read status"], ignoreOrder: true);
    }

    [Then(@"^it references no project of the notification service$")]
    public void ThenItReferencesNoProjectOfTheNotificationService()
    {
        var dependencies = Nuspec.Descendants().Where(e => e.Name.LocalName == "dependency")
            .Select(e => (string?)e.Attribute("id") ?? string.Empty)
            .ToList();
        // The client does depend on Refit, so the dependency list was read.
        dependencies.ShouldContain("Refit");
        dependencies.ShouldAllBe(id => !id.StartsWith("DKNet.Notification", StringComparison.OrdinalIgnoreCase));
    }

    [Then(@"^its project and repository links point at the DKNet.Notification.Api repo$")]
    public void ThenItsProjectAndRepositoryLinksPointAtTheRepo()
    {
        Metadata("projectUrl").ShouldBe(RepoUrl);
        var repository = Nuspec.Descendants().Single(e => e.Name.LocalName == "repository");
        // A git remote may end in ".git" or "/"; it is the same repo (workspace repo matching rule).
        var url = ((string?)repository.Attribute("url")).ShouldNotBeNull();
        url = url.TrimEnd('/');
        url = url.EndsWith(".git", StringComparison.OrdinalIgnoreCase) ? url[..^4] : url;
        url.ShouldBe(RepoUrl);
    }

    #endregion

    #region The service's own projects

    [Given(@"^the notification service's API, application, domain and shared projects$")]
    public void GivenTheNotificationServicesApiApplicationDomainAndSharedProjects() =>
        ServiceProjects.ShouldAllBe(project => File.Exists(Path.Combine(RepoRoot(), project)));

    [When(@"^their package and project references are checked$")]
    public void WhenTheirPackageAndProjectReferencesAreChecked() =>
        _references = ServiceProjects
            .SelectMany(project => XDocument.Load(Path.Combine(RepoRoot(), project)).Descendants()
                .Where(e => e.Name.LocalName is "PackageReference" or "ProjectReference")
                // A path may use either separator; the file name is compared.
                .Select(e => (project, e.Name.LocalName, ((string?)e.Attribute("Include")).ShouldNotBeNull().Replace('\\', '/'))))
            .ToList();

    [Then(@"^none of them depends on the client package$")]
    public void ThenNoneOfThemDependsOnTheClientPackage()
    {
        // The API does reference the application project, so the references were read.
        _references.ShouldContain(r => r.Kind == "ProjectReference"
                                       && Path.GetFileName(r.Include) == "DKNet.Notification.AppServices.csproj");
        _references.ShouldAllBe(r => !string.Equals(
            Path.GetFileNameWithoutExtension(r.Include),
            "DKNet.Notification.Client",
            StringComparison.OrdinalIgnoreCase));
    }

    [Then(@"^none of them depends on Refit$")]
    public void ThenNoneOfThemDependsOnRefit() =>
        _references.Where(r => r.Kind == "PackageReference")
            .ShouldAllBe(r => !string.Equals(r.Include, "Refit", StringComparison.OrdinalIgnoreCase)
                              && !r.Include.StartsWith("Refit.", StringComparison.OrdinalIgnoreCase));

    #endregion

    private XElement Nuspec => _nuspec.ShouldNotBeNull("a step must open the package first");

    private string? Metadata(string name) =>
        Nuspec.Descendants().FirstOrDefault(e => e.Name.LocalName == name)?.Value;

    /// <summary>The folder that holds <c>DKNet.Notification.sln</c>, found above the test output.</summary>
    private static string RepoRoot()
    {
        for (var folder = new DirectoryInfo(AppContext.BaseDirectory); folder is not null; folder = folder.Parent)
        {
            if (File.Exists(Path.Combine(folder.FullName, "DKNet.Notification.sln")))
            {
                return folder.FullName;
            }
        }

        throw new InvalidOperationException("DKNet.Notification.sln is not above the test output.");
    }

    /// <summary>Runs the SDK the test runs on (<c>DOTNET_HOST_PATH</c>), with a time limit.</summary>
    private static async Task<(int ExitCode, string Output)> DotnetAsync(params string[] arguments)
    {
        var start = new ProcessStartInfo(Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = RepoRoot()
        };
        foreach (var argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }

        using var process = Process.Start(start).ShouldNotBeNull();
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        using var limit = new CancellationTokenSource(TimeSpan.FromMinutes(5));
        await process.WaitForExitAsync(limit.Token);
        return (process.ExitCode, await output + await error);
    }
}
