using System.Diagnostics.CodeAnalysis;
using NetArchTest.Rules;
using DKNet.Notification.App.Tests.Extensions;

namespace DKNet.Notification.App.Tests.Architecture;

public class ApiTests
{
    [Fact]
    public void AllApiClassesShouldBeInternal()
    {
        // Adjust the assembly name if needed
        var types = Types.InAssembly(typeof(DKNet.Notification.Api.Program).Assembly);

        var result = types
            .That()
            .AreClasses()
            .And().DoNotHaveName(nameof(DKNet.Notification.Api.Program))
            .AndNotSystemGeneratedClasses()
            .Should()
            .NotBePublic()
            .GetResult();

        result.ShouldNotBeNull("There is No classes found.");
        result.IsSuccessful.ShouldBeTrue(
            $"These classes should be internal: {string.Join(", ", (result.FailingTypes ?? []).Select(t => t.FullName))}");
    }

    [Fact]
    public void AllConfigsClassesShouldBeStaticAndExcludedFromCodeCoverage()
    {
        // Adjust the assembly name if needed
        var types = Types.InAssembly(typeof(DKNet.Notification.Api.Program).Assembly);

        var result = types
            .That()
            .AreClasses()
            .And()
            .HaveNameEndingWith("Config", StringComparison.OrdinalIgnoreCase)
            .Should()
            .BeStatic()
            .And().HaveCustomAttribute(typeof(ExcludeFromCodeCoverageAttribute))
            .GetResult();

        result.IsSuccessful.ShouldBeTrue(
            $"These classes should be static and excluded from code coverage: {string.Join(", ", (result.FailingTypes ?? []).Select(t => t.FullName))}");
    }

    // [Fact]
    // public void AllEndPointClassesShouldBeInternalAndSealed()
    // {
    //     // Adjust the assembly name if needed
    //     var types = Types.InAssembly(typeof(DKNet.Notification.Api.Program).Assembly);
    //
    //     var result = types
    //         .That()
    //         .AreClasses()
    //         .And()
    //         .HaveNameEndingWith("Endpoint", StringComparison.OrdinalIgnoreCase)
    //         .Should().NotBePublic()
    //         .And().BeSealed()
    //         .GetResult();
    //
    //     result.IsSuccessful.ShouldBeTrue(
    //         $"These classes should be sealed and internal: {string.Join(", ", (result.FailingTypes ?? []).Select(t => t.FullName))}");
    // }

    [Fact]
    public void AllConfigClassesShouldExcludeFromCodeCoverage()
    {
        // Adjust the assembly name if needed
        var types = Types.InAssembly(typeof(DKNet.Notification.Api.Program).Assembly);

        var result = types
            .That()
            .AreClasses()
            .And().AreStatic()
            .And().HaveNameEndingWith("Config", StringComparison.OrdinalIgnoreCase)
            .Should().NotBePublic()
            .And().BeSealed()
            .And().HaveCustomAttribute(typeof(ExcludeFromCodeCoverageAttribute))
            .GetResult();

        result.IsSuccessful.ShouldBeTrue(
            $"These classes should be excluded from ChargeCode Coverage: {string.Join(", ", (result.FailingTypes ?? []).Select(t => t.FullName))}");
    }

    [Fact]
    public void AllEndpointClassesShouldBeInternalAndSealed_ExceptAbstractClasses()
    {
        var types = Types.InAssembly(typeof(DKNet.Notification.Api.Program).Assembly);

        // Get all classes in ApiEndpoints namespace and sub-namespaces
        var endpointClasses = types
            .That()
            .AreClasses()
            .And().ResideInNamespaceContaining("ApiEndpoints")
            .GetTypes()
            .ToList();

        // Separate abstract and concrete classes
        var abstractClasses = endpointClasses.Where(t => t.IsAbstract).ToList();
        var concreteClasses = endpointClasses.Where(t => !t.IsAbstract).ToList();

        // Check abstract classes are internal
        var nonInternalAbstractClasses = abstractClasses
            .Where(t => !t.IsNotPublic && !t.IsNestedAssembly)
            .Select(t => t.FullName)
            .ToList();

        nonInternalAbstractClasses.ShouldBeEmpty(
            $"Abstract endpoint classes must be internal. Offenders: {string.Join(", ", nonInternalAbstractClasses)}");

        // Check concrete classes are internal AND sealed
        var nonInternalConcreteClasses = concreteClasses
            .Where(t => !t.IsNotPublic && !t.IsNestedAssembly)
            .Select(t => t.FullName)
            .ToList();

        nonInternalConcreteClasses.ShouldBeEmpty(
            $"Concrete endpoint classes must be internal. Offenders: {string.Join(", ", nonInternalConcreteClasses)}");

        var nonSealedConcreteClasses = concreteClasses
            .Where(t => !t.IsSealed)
            .Select(t => t.FullName)
            .ToList();

        nonSealedConcreteClasses.ShouldBeEmpty(
            $"Concrete endpoint classes must be sealed. Offenders: {string.Join(", ", nonSealedConcreteClasses)}");
    }
}