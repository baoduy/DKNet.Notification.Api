using System.Reflection;
using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Testing;
using Shouldly;

namespace DKNet.Notification.App.Tests.Scaffold;

/// <summary>
/// DRK-1994 §5 scenario "The local run starts Redis and the API, and no database" (<c>@integration</c>). It lives
/// here, not in the Reqnroll suite, because the spec allows <c>Aspire.Hosting.Testing</c> in App.Tests only (Q1).
/// The AppHost's resource model is built exactly as <c>dotnet run</c> builds it, but never started: the resources
/// it would run are read from the model, so no container runtime is needed.
/// </summary>
public sealed class LocalAppHostTests
{
    [Fact]
    public async Task TheLocalRunStartsRedisAndTheApi_AndNoDatabase()
    {
        // Loaded by name, so this test does not depend on any type the AppHost declares.
        var appHostEntryPoint = Assembly.Load(new AssemblyName("DKNet.Notification.AppHost")).EntryPoint!.DeclaringType!;
        var builder = await DistributedApplicationTestingBuilder.CreateAsync(appHostEntryPoint);
        await using var app = await builder.BuildAsync();
        var resources = app.Services.GetRequiredService<DistributedApplicationModel>().Resources;

        // A local start runs every resource except those without a lifetime (parameters, connection strings) and
        // those started only on demand — Aspire's hidden "<project>-rebuilder" helper is one of the latter.
        var run = resources
            .Where(r => r is not IResourceWithoutLifetime && !r.Annotations.OfType<ExplicitStartupAnnotation>().Any())
            .ToArray();
        run.Select(r => r.Name).Order(StringComparer.Ordinal).ShouldBe(["Api", "Redis"]);
        run.Single(r => r.Name == "Api").ShouldBeOfType<ProjectResource>();
        run.Single(r => r.Name == "Redis").ShouldBeAssignableTo<ContainerResource>();

        resources
            .Where(r => r.GetType().Name.EndsWith("DatabaseResource", StringComparison.Ordinal) ||
                        r.GetType().Name.EndsWith("ServerResource", StringComparison.Ordinal))
            .Select(r => $"{r.Name} ({r.GetType().Name})")
            .ShouldBeEmpty();
    }
}
