using System.Diagnostics;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using NetArchTest.Rules;

namespace DKNet.Notification.App.Tests.Scaffold;

/// <summary>
/// DRK-1994 §5 — the <c>@unit</c> scenarios of "DKNet Notification starts as an empty service", one test per
/// scenario, named after it. Every expected value is a literal from the spec; the checks read the repo, the
/// project files, the compiled assemblies or the running host, never a production type that Build removes.
/// </summary>
public sealed class EmptyServiceScaffoldTests
{
    private const RegexOptions IgnoreCase = RegexOptions.IgnoreCase | RegexOptions.CultureInvariant;

    /// <summary>Package-id patterns of every part §3 removes, labelled with the part they belong to.</summary>
    private static readonly (string Part, Regex PackageId)[] RemovedPackageParts =
    [
        ("database driver", new Regex(
            @"^(Npgsql|Microsoft\.Data\.SqlClient|System\.Data\.SqlClient|Microsoft\.Data\.Sqlite|MySqlConnector|MySql\.Data|Oracle\.)",
            IgnoreCase)),
        ("database server", new Regex("PostgreSql", IgnoreCase)),
        ("EF Core", new Regex(@"^Microsoft\.EntityFrameworkCore", IgnoreCase)),
        ("DKNet.EfCore", new Regex(@"^DKNet\.EfCore\.", IgnoreCase)),
        ("DKNet.SlimBus", new Regex(@"^DKNet\.SlimBus\.", IgnoreCase)),
        ("message bus", new Regex(@"^(SlimMessageBus|Azure\.Messaging\.ServiceBus)", IgnoreCase)),
        ("typed client", new Regex(@"^Refit", IgnoreCase))
    ];

    /// <summary>Namespaces of the removed parts' types (§3: EF Core, DKNet.EfCore, the message bus).</summary>
    private static readonly string[] RemovedPartNamespaces =
        ["Microsoft.EntityFrameworkCore", "DKNet.EfCore", "SlimMessageBus", "DKNet.SlimBus"];

    /// <summary>Words that name a removed part in a settings file or a document.</summary>
    private static readonly (string Part, Regex Word)[] RemovedPartWords =
    [
        ("the products sample", new Regex(@"\bproducts?\b", IgnoreCase)),
        ("the purchase orders sample", new Regex(@"purchase[ -]?orders?", IgnoreCase)),
        ("the samples' generated data", new Regex("SampleData", IgnoreCase)),
        ("PostgreSQL", new Regex(@"postgre|npgsql|\bAppDb\b|DbMigration", IgnoreCase)),
        ("EF Core", new Regex(@"\bEF ?Core|EntityFramework", IgnoreCase)),
        ("the message bus", new Regex("message ?bus|SlimBus|ServiceBus|AzureBus|busConfig", IgnoreCase))
    ];

    /// <summary>Setting keys that hold a connection string, password or client secret.</summary>
    private static readonly Regex SecretKey = new("ConnectionString|Password|Secret", IgnoreCase);

    /// <summary>The approved design documents (DRK-1994 §3: they do not change and are not searched).</summary>
    private const string DesignDocumentsFolder = "docs/architect/";

    private static readonly string[] DesignDKNetPackages =
        ["DKNet.AspCore.Extensions", "DKNet.AspCore.Idempotency", "DKNet.AspCore.Idempotency.RedisStore"];

    private const string DesignDKNetPackageVersion = "13.1.3";

    #region Scenario: The solution references no removed part

    [Fact]
    public void TheSolutionReferencesNoRemovedPart()
    {
        var references = DirectPackageReferences().Concat(CentralPackageVersions().Select(v => (File: "Directory.Packages.props", v.Id))).ToArray();
        references.ShouldNotBeEmpty();

        var offenders = references
            .SelectMany(r => RemovedPackageParts
                .Where(p => p.PackageId.IsMatch(r.Id))
                .Select(p => $"{r.File}: {r.Id} ({p.Part})"))
            .Concat(ScaffoldRepo.SolutionProjects
                .SelectMany(project => XDocument.Load(project).Descendants("ProjectReference")
                    .Select(e => e.Attribute("Include")?.Value ?? string.Empty)
                    .Where(include => include.EndsWith(".Client.csproj", StringComparison.OrdinalIgnoreCase))
                    .Select(include => $"{Path.GetFileName(project)}: {include} (typed client)")))
            .Distinct()
            .ToArray();

        offenders.ShouldBeEmpty($"Removed parts still referenced:{Environment.NewLine}{string.Join(Environment.NewLine, offenders)}");
    }

    /// <summary>§5 contract row "package" — the only direct DKNet packages are the 3 the design names, at 13.1.3.</summary>
    [Fact]
    public void TheOnlyDirectDKNetPackagesAreTheThreeTheDesignNames_At13_1_3()
    {
        DirectPackageReferences()
            .Select(r => r.Id)
            .Where(id => id.StartsWith("DKNet.", StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.Ordinal)
            .ShouldBe(DesignDKNetPackages);

        CentralPackageVersions()
            .Where(v => v.Id.StartsWith("DKNet.", StringComparison.OrdinalIgnoreCase))
            .OrderBy(v => v.Id, StringComparer.Ordinal)
            .Select(v => $"{v.Id} {v.Version}")
            .ShouldBe(DesignDKNetPackages.Select(id => $"{id} {DesignDKNetPackageVersion}"));
    }

    /// <summary>§5 contract row "test package" — <c>Aspire.Hosting.Testing</c> 13.5.4 in App.Tests only (DRK-1994 Q1).</summary>
    [Fact]
    public void AspireHostingTestingIsReferencedByAppTestsOnly_At13_5_4()
    {
        DirectPackageReferences()
            .Where(r => r.Id == "Aspire.Hosting.Testing")
            .Select(r => r.File)
            .ShouldBe(["DKNet.Notification.App.Tests.csproj"]);

        CentralPackageVersions().Where(v => v.Id == "Aspire.Hosting.Testing").Select(v => v.Version)
            .ShouldBe(["13.5.4"]);
    }

    #endregion

    #region Scenario: The service's own code uses no EF Core or message bus

    [Fact]
    public void TheServicesOwnCodeUsesNoEfCoreOrMessageBus()
    {
        var types = Types.InAssemblies(ScaffoldRepo.ServiceAssemblies());
        types.GetTypes().ShouldNotBeEmpty();

        var result = types.ShouldNot().HaveDependencyOnAny(RemovedPartNamespaces).GetResult();

        result.IsSuccessful.ShouldBeTrue(
            "These types use EF Core, a DKNet.EfCore package or the message bus: " +
            string.Join(", ", (result.FailingTypes ?? []).Select(t => t.FullName)));
    }

    #endregion

    #region Scenario: No settings file or document names a removed part

    [Fact]
    public void NoSettingsFileOrDocumentNamesARemovedPart()
    {
        var files = SettingsFiles().Concat(Documents()).ToArray();
        files.ShouldContain("ApiEndpoints/DKNet.Notification.Api/appsettings.json");
        files.ShouldContain("AGENTS.md");

        var offenders = files
            .SelectMany(file => File.ReadLines(ScaffoldRepo.FullPath(file))
                .Select((text, index) => (Text: text, Line: index + 1))
                .SelectMany(line => RemovedPartWords
                    .Select(word => (word.Part, Match: word.Word.Match(line.Text)))
                    .Where(hit => hit.Match.Success)
                    .Select(hit => $"{file}:{line.Line}: \"{hit.Match.Value}\" names {hit.Part}")))
            .ToArray();

        offenders.ShouldBeEmpty($"Removed parts still named:{Environment.NewLine}{string.Join(Environment.NewLine, offenders)}");
    }

    #endregion

    #region Scenario: No settings file holds a secret

    [Fact]
    public void NoSettingsFileHoldsASecret()
    {
        var entries = SettingsFiles()
            .SelectMany(file => SecretEntries(ParseSettings(file), file))
            .ToArray();
        entries.ShouldContain(e => e.Key == "ApiEndpoints/DKNet.Notification.Api/appsettings.json:AzureMonitor:ConnectionString");

        var filled = entries.Where(e => e.Value.Length > 0).Select(e => e.Key).ToArray();

        filled.ShouldBeEmpty($"These settings hold a value: {string.Join(", ", filled)}");
    }

    #endregion

    #region Scenario: The solution publishes no package

    [Fact]
    public async Task TheSolutionPublishesNoPackage()
    {
        var output = Directory.CreateTempSubdirectory("drk1994-pack-");
        try
        {
            var (exitCode, log) = await RunDotnetAsync(
                "pack", ScaffoldRepo.FullPath(ScaffoldRepo.SolutionFileName),
                "--configuration", BuildConfiguration(),
                "--no-build", "--no-restore", "--nologo",
                "--output", output.FullName);

            exitCode.ShouldBe(0, log);
            output.GetFiles("*.nupkg").Concat(output.GetFiles("*.snupkg")).Select(f => f.Name).ShouldBeEmpty(log);
        }
        finally
        {
            output.Delete(true);
        }
    }

    #endregion

    #region Scenario: Entra ID bearer token is the only sign-in method

    [Fact]
    public async Task EntraIdBearerTokenIsTheOnlySignInMethod()
    {
        // Deployed (base) settings: the JWT bearer handler, which validates Entra ID tokens, and nothing else.
        (await SignInMethodsAsync("Production")).ShouldBe([("Bearer", typeof(JwtBearerHandler))]);

        // Local Development and test settings keep sign-in off — and no other sign-in method takes its place.
        foreach (var environment in new[] { "Development", "Testing" })
        {
            var methods = await SignInMethodsAsync(environment);
            methods.Where(m => m != ("Bearer", typeof(JwtBearerHandler))).ShouldBeEmpty(
                $"{environment} registers: {string.Join(", ", methods)}");
        }

        // No demo sign-in method exists, registered or not: the service's own code holds no sign-in handler.
        ScaffoldRepo.ServiceAssemblies()
            .SelectMany(a => a.GetTypes())
            .Where(t => typeof(IAuthenticationHandler).IsAssignableFrom(t))
            .Select(t => t.FullName)
            .ShouldBeEmpty();
    }

    #endregion

    #region Helpers

    private static IEnumerable<(string File, string Id)> DirectPackageReferences() =>
        ScaffoldRepo.SolutionProjects.SelectMany(project => XDocument.Load(project).Descendants("PackageReference")
            .Select(e => (File: Path.GetFileName(project), Id: e.Attribute("Include")?.Value ?? string.Empty)));

    private static IEnumerable<(string Id, string Version)> CentralPackageVersions() =>
        XDocument.Load(ScaffoldRepo.FullPath("Directory.Packages.props"))
            .Descendants()
            .Where(e => e.Name.LocalName is "PackageVersion" or "GlobalPackageReference")
            .Select(e => (Id: e.Attribute("Include")?.Value ?? string.Empty, Version: e.Attribute("Version")?.Value ?? string.Empty));

    private static IEnumerable<string> SettingsFiles() =>
        ScaffoldRepo.Files(".json").Where(f => !f.StartsWith(DesignDocumentsFolder, StringComparison.Ordinal));

    private static IEnumerable<string> Documents() =>
        ScaffoldRepo.Files(".md").Where(f => !f.StartsWith(DesignDocumentsFolder, StringComparison.Ordinal));

    private static JsonElement ParseSettings(string file)
    {
        using var document = JsonDocument.Parse(
            File.ReadAllText(ScaffoldRepo.FullPath(file)),
            new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
        return document.RootElement.Clone();
    }

    /// <summary>
    /// Every string setting that holds a connection string, password or client secret: each value under a
    /// <c>ConnectionStrings</c> section, and each value whose key names a connection string, password or secret.
    /// </summary>
    private static IEnumerable<(string Key, string Value)> SecretEntries(JsonElement element, string path, bool inConnectionStrings = false)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            yield break;
        }

        foreach (var property in element.EnumerateObject())
        {
            var key = $"{path}:{property.Name}";
            if (property.Value.ValueKind == JsonValueKind.Object)
            {
                foreach (var entry in SecretEntries(property.Value, key,
                             property.Name.Equals("ConnectionStrings", StringComparison.OrdinalIgnoreCase)))
                {
                    yield return entry;
                }
            }
            else if (property.Value.ValueKind == JsonValueKind.String &&
                     (inConnectionStrings || SecretKey.IsMatch(property.Name)))
            {
                yield return (key, property.Value.GetString()!);
            }
        }
    }

    /// <summary>The configuration this test assembly was built in (<c>bin/&lt;Configuration&gt;/&lt;tfm&gt;/</c>).</summary>
    private static string BuildConfiguration() =>
        new DirectoryInfo(Path.TrimEndingDirectorySeparator(AppContext.BaseDirectory)).Parent!.Name;

    private static async Task<(int ExitCode, string Log)> RunDotnetAsync(params string[] arguments)
    {
        var startInfo = new ProcessStartInfo(Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = ScaffoldRepo.Root
        };
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        // The test host inherits MSBuild's own variables from "dotnet test"; a child build must not reuse them.
        foreach (var key in startInfo.Environment.Keys.Where(k => k.StartsWith("MSBuild", StringComparison.OrdinalIgnoreCase)).ToArray())
        {
            startInfo.Environment.Remove(key);
        }

        using var process = Process.Start(startInfo)!;
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(5));
        var stdout = process.StandardOutput.ReadToEndAsync(timeout.Token);
        var stderr = process.StandardError.ReadToEndAsync(timeout.Token);
        await process.WaitForExitAsync(timeout.Token);
        return (process.ExitCode, await stdout + await stderr);
    }

    private static async Task<(string Name, Type? HandlerType)[]> SignInMethodsAsync(string environment)
    {
        await using var factory = new WebApplicationFactory<DKNet.Notification.Api.Program>()
            .WithWebHostBuilder(builder => builder.UseEnvironment(environment));
        var provider = factory.Services.GetService<IAuthenticationSchemeProvider>();
        if (provider is null)
        {
            return [];
        }

        return (await provider.GetAllSchemesAsync()).Select(s => (s.Name, (Type?)s.HandlerType)).ToArray();
    }

    #endregion
}
