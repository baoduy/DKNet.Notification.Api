using FluentValidation;
using NetArchTest.Rules;
using DKNet.Notification.AppServices;
using DKNet.Notification.Domains.Share;
using SlimMessageBus;
using Xunit.Abstractions;

namespace DKNet.Notification.App.Tests.Architecture;

public class AppServiceTests(ITestOutputHelper outputHelper)
{
    #region Methods

    [Fact]
    public void AllHandlerClassesShouldBeInternalAndSealed()
    {
        // Adjust the assembly name if needed
        var types = Types.InAssembly(typeof(AppSetup).Assembly);

        var result = types
            .That()
            .AreClasses().And().AreNotAbstract()
            .And().ImplementInterface(typeof(IRequestHandler<>))
            .Or().AreClasses().And().AreNotAbstract()
            .And().ImplementInterface(typeof(IRequestHandler<,>))
            .Or().AreClasses().And().AreNotAbstract()
            .And().ImplementInterface(typeof(IConsumer<>))
            .Should().NotBePublic()
            .And().BeSealed()
            .GetResult();

        result.IsSuccessful.ShouldBeTrue(
            $"These handler classes should be internal sealed classes: {string.Join(", ", (result.FailingTypes ?? []).Select(t => t.FullName))}");
    }

    [Fact]
    public void AllRepoInterfaces_ShouldInheritFromIRepository()
    {
        var assembly = typeof(AppSetup).Assembly;
        var repoInterfaces = assembly.GetTypes()
            .Where(t => t.IsInterface &&
                        (t.Name.EndsWith("Repo", StringComparison.Ordinal) ||
                         t.Name.EndsWith("Repository", StringComparison.Ordinal)))
            .ToList();

        var violations = new List<string>();
        foreach (var iface in repoInterfaces)
        {
            var hasIRepository = iface.GetInterfaces().Any(i =>
                i.IsGenericType && i.GetGenericTypeDefinition().Name == "IRepository`1");
            if (!hasIRepository)
                violations.Add(iface.FullName ?? iface.Name);
        }

        violations.ShouldBeEmpty(
            $"The following interfaces do not inherit from IRepository<TEntity>:\n\t{string.Join("\n\t", violations)}");
    }

    [Fact]
    public void AllValidatorClassesShouldBeInternalAndSealed()
    {
        // Adjust the assembly name if needed
        var types = Types.InAssembly(typeof(AppSetup).Assembly);

        var result = types
            .That()
            .AreClasses()
            .And().AreNotAbstract()
            .And().Inherit(typeof(AbstractValidator<>))
            .And().DoNotHaveNameStartingWith("CustomerRequestValidator")
            .And().DoNotHaveNameStartingWith("OrderRequestValidator")
            .Should().NotBePublic()
            .And().BeSealed()
            .GetResult();

        result.IsSuccessful.ShouldBeTrue(
            $"These handler classes should be sealed and internal: \n\t{string.Join(", ", (result.FailingTypes ?? []).Select(t => t.Name))}");
    }

    [Fact]
    public void DtosWithGenerateDtoAttribute_ShouldNotHaveProperties_ThatAreDomainEntities()
    {
        // Arrange: Get all types from AppServices assembly that have GenerateDto attribute
        var assembly = typeof(AppSetup).Assembly;
        var dtoTypes = assembly.GetTypes()
            .Where(t => t.IsClass &&
                        t.GetCustomAttributes(false)
                            .Any(a => a.GetType().Name == "GenerateDtoAttribute"))
            .ToList();

        outputHelper.WriteLine($"Found {dtoTypes.Count} types with GenerateDto attribute");

        var violations = new List<string>();

        // Act: Check each DTO type for properties that are DomainEntity
        foreach (var dtoType in dtoTypes)
        {
            outputHelper.WriteLine($"\nChecking: {dtoType.Name}");

            var properties = dtoType.GetProperties();
            foreach (var property in properties)
            {
                var propertyType = property.PropertyType;

                // Handle collections (ICollection<T>, IEnumerable<T>, List<T>, etc.)
                if (propertyType.IsGenericType)
                {
                    var genericArgs = propertyType.GetGenericArguments();
                    foreach (var genericArg in genericArgs)
                        if (IsDomainEntity(genericArg))
                        {
                            var violation =
                                $"{dtoType.Name}.{property.Name} has property type {genericArg.Name} which inherits from DomainEntity";
                            violations.Add(violation);
                            outputHelper.WriteLine($"  ❌ VIOLATION: {violation}");
                        }
                }
                // Handle direct property types
                else if (IsDomainEntity(propertyType))
                {
                    var violation =
                        $"{dtoType.Name}.{property.Name} has property type {propertyType.Name} which inherits from DomainEntity";
                    violations.Add(violation);
                    outputHelper.WriteLine($"  ❌ VIOLATION: {violation}");
                }
            }
        }

        // Assert: No violations should be found
        violations.ShouldBeEmpty(
            $"The following DTOs have properties that are DomainEntity types:\n\t{string.Join("\n\t", violations)}\n\n" +
            "DTOs should not expose domain entities directly. Use proper DTO types instead.");
    }

    private static bool IsDomainEntity(Type? type)
    {
        if (type == null || type == typeof(object))
            return false;

        // Check if the type is DomainEntity or inherits from it
        var currentType = type;
        while (currentType != null && currentType != typeof(object))
        {
            if (currentType == typeof(DomainEntity))
                return true;

            currentType = currentType.BaseType;
        }

        return false;
    }

    #endregion
}