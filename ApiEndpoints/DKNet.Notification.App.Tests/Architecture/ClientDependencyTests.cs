using System.Xml.Linq;
using NetArchTest.Rules;

namespace DKNet.Notification.App.Tests.Architecture;

/// <summary>
/// DRK-1789 R1 — Scenario: Only the tests depend on the client. The client serves downstream systems
/// only: no project of the solution depends on it except the tests that verify it, and the client itself
/// references no project of the solution.
/// </summary>
public class ClientDependencyTests
{
    private const string ClientNamespace = "DKNet.Notification.Client";

    private static readonly string ApiEndpointsDir =
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../.."));

    public static TheoryData<string> ServiceAssemblies() =>
    [
        typeof(DKNet.Notification.Api.Program).Assembly.GetName().Name!,
        typeof(DKNet.Notification.AppServices.AutomatedSample.V1.ProductDto).Assembly.GetName().Name!,
        typeof(DKNet.Notification.Domains.Features.AutomatedSample.Entities.Product).Assembly.GetName().Name!,
        typeof(DKNet.Notification.Infra.Contexts.CoreDbContext).Assembly.GetName().Name!,
        typeof(DKNet.Notification.Share.SharedConsts).Assembly.GetName().Name!
    ];

    [Theory]
    [MemberData(nameof(ServiceAssemblies))]
    public void ServiceAssembly_DoesNotDependOnTheClient(string assemblyName)
    {
        var assembly = AppDomain.CurrentDomain.GetAssemblies().Single(a => a.GetName().Name == assemblyName);

        var result = Types.InAssembly(assembly)
            .ShouldNot().HaveDependencyOn(ClientNamespace)
            .GetResult();

        result.IsSuccessful.ShouldBeTrue(
            $"{assemblyName} types depend on the client: " +
            string.Join(", ", (result.FailingTypes ?? []).Select(t => t.FullName)));
    }

    [Fact]
    public void OnlyTestProjects_ReferenceTheClientProject()
    {
        var referencing = Directory.GetFiles(ApiEndpointsDir, "*.csproj", SearchOption.AllDirectories)
            .Where(f => !f.Split(Path.DirectorySeparatorChar).Any(s => s is "bin" or "obj"))
            .Select(f => (Name: Path.GetFileNameWithoutExtension(f), Csproj: XDocument.Load(f)))
            .Where(p => p.Csproj.Descendants("ProjectReference").Any(r =>
                (r.Attribute("Include")?.Value ?? string.Empty).Replace('\\', '/')
                .EndsWith($"/{ClientNamespace}.csproj", StringComparison.Ordinal)))
            .ToList();

        referencing.Select(p => p.Name).ShouldContain("DKNet.Notification.App.Tests");
        var nonTestProjects = referencing
            .Where(p => !p.Csproj.Descendants("PackageReference")
                .Any(r => r.Attribute("Include")?.Value == "Microsoft.NET.Test.Sdk"))
            .Select(p => p.Name)
            .ToList();
        nonTestProjects.ShouldBeEmpty(
            "only test projects may reference the client: " + string.Join(", ", nonTestProjects));
    }

    [Fact]
    public void ClientProject_ReferencesNoProjectOfTheSolution()
    {
        var clientCsproj = Path.Combine(ApiEndpointsDir, ClientNamespace, $"{ClientNamespace}.csproj");
        File.Exists(clientCsproj).ShouldBeTrue(clientCsproj);

        var projectReferences = XDocument.Load(clientCsproj).Descendants("ProjectReference")
            .Select(r => r.Attribute("Include")?.Value)
            .ToList();

        projectReferences.ShouldBeEmpty(
            "the client carries its own contracts and references no project: " + string.Join(", ", projectReferences));
    }
}
