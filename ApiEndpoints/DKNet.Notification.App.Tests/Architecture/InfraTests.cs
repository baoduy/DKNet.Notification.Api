using DKNet.EfCore.Abstractions.Events;
using DKNet.EfCore.Extensions.Configurations;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using NetArchTest.Rules;
using DKNet.Notification.Infra.Contexts;
using DKNet.Notification.Infra.Extensions;
using DKNet.Notification.Infra.Services;
using SlimMessageBus;

namespace DKNet.Notification.App.Tests.Architecture;

public class InfraTests
{
    #region Methods

    /// <summary>
    /// DRK-1219 R2: <c>UseAutoDataSeeding</c>'s post-migration hook resolves <see cref="EventPublisher"/> —
    /// which needs a real <c>IMessageBus</c> to construct — from the same design-time provider
    /// <see cref="DbContextFactory"/> builds. Before the fix, this provider had no bus registered, so
    /// resolving <see cref="IEventPublisher"/> threw a missing-service exception and
    /// <c>dotnet ef database update</c> died while running seeding.
    /// </summary>
    [Fact]
    public void DesignTimeServiceProvider_CanActivateEventPublisher()
    {
        var serviceProvider = DbContextFactory.BuildServiceProvider([]);
        using var disposable = serviceProvider as IDisposable;

        var bus = serviceProvider.GetRequiredService<IMessageBus>();
        var publisher = serviceProvider.GetRequiredService<IEventPublisher>();

        bus.ShouldNotBeNull();
        publisher.ShouldNotBeNull();
        publisher.ShouldBeOfType<EventPublisher>();
    }

    [Fact]
    public void AllEfConfigClassesShouldBeInternalAndSealed()
    {
        // Adjust the assembly name if needed
        var types = Types.InAssembly(typeof(InfraSetup).Assembly);

        var result = types
            .That()
            .AreClasses()
            .And()
            .Inherit(typeof(IEntityTypeConfiguration<>))
            .Should().NotBePublic()
            .And().BeSealed()
            .GetResult();

        result.IsSuccessful.ShouldBeTrue(
            $"These EfCore Config classes should be sealed and internal: {string.Join(", ", (result.FailingTypes ?? []).Select(t => t.FullName))}");
    }

    [Fact]
    public void AllEnumProperties_StoringToDb_ShouldHaveStringConversion()
    {
        using var dbContext = new DbContextFactory().CreateDbContext([]);
        var model = dbContext.Model;
        var violations = new List<string>();
        var seenProperties = new HashSet<string>();

        foreach (var entityType in model.GetEntityTypes())
        foreach (var property in entityType.GetProperties())
            // Check if property is an enum type
            if (property.ClrType.IsEnum)
            {
                // Create a unique key to avoid duplicate violations for owned entities
                var key = $"{entityType.ClrType.FullName}.{property.Name}";
                if (seenProperties.Contains(key))
                    continue;

                seenProperties.Add(key);

                // Check if the provider CLR type is String (which means it's converted to string)
                var providerClrType = property.GetProviderClrType();

                if (providerClrType != typeof(string))
                    violations.Add(
                        $"{entityType.ClrType.Name}.{property.Name} (Type: {property.ClrType.Name}) - " +
                        $"Expected ProviderClrType=String but got {providerClrType?.Name ?? "NULL"}. " +
                        $"Must be configured with HasConversion<string>()");
            }

        violations.ShouldBeEmpty(
            "The following enum properties MUST be configured with HasConversion<string>():\n" +
            string.Join("\n", violations));
    }

    [Fact]
    public void AllHandlerClassesShouldBeInternalAndSealed()
    {
        // Adjust the assembly name if needed
        var types = Types.InAssembly(typeof(InfraSetup).Assembly);

        var result = types
            .That()
            .AreClasses()
            .And().ImplementInterface(typeof(IRequestHandler<>))
            .Or().ImplementInterface(typeof(IRequestHandler<,>))
            .Or().ImplementInterface(typeof(IConsumer<>))
            .Should().NotBePublic()
            .And().BeSealed()
            .GetResult();

        result.IsSuccessful.ShouldBeTrue(
            $"These handler classes should be sealed and internal: {string.Join(", ", (result.FailingTypes ?? []).Select(t => t.FullName))}");
    }

    [Fact]
    public void AllSeedingDataClassesShouldBeInternalAndSealed()
    {
        // Adjust the assembly name if needed
        var types = Types.InAssembly(typeof(InfraSetup).Assembly);

        var result = types
            .That()
            .AreClasses()
            .And().ImplementInterface(typeof(IDataSeedingConfiguration))
            .Should().NotBePublic()
            .And().BeSealed()
            .GetResult();

        result.IsSuccessful.ShouldBeTrue(
            $"These EfCore Config classes should be sealed and internal: {string.Join(", ", (result.FailingTypes ?? []).Select(t => t.FullName))}");
    }

    [Fact]
    public void AllValidatorClassesShouldBeInternalAndSealed()
    {
        // Adjust the assembly name if needed
        var types = Types.InAssembly(typeof(InfraSetup).Assembly);

        var result = types
            .That()
            .AreClasses()
            .And()
            .Inherit(typeof(AbstractValidator<>))
            .Should().NotBePublic()
            .And().BeSealed()
            .GetResult();

        result.IsSuccessful.ShouldBeTrue(
            $"These handler classes should be sealed and internal: {string.Join(", ", (result.FailingTypes ?? []).Select(t => t.FullName))}");
    }

    [Fact]
    public void NoEntityString_ShouldBe_ConfiguredAs_Max()
    {
        using var dbContext = new DbContextFactory().CreateDbContext([]);
        var model = dbContext.Model;
        var violations = new List<string>();

        foreach (var entityType in model.GetEntityTypes())
        foreach (var property in entityType.GetProperties())
            // Check if property is a string type
            if (property.ClrType == typeof(string))
            {
                var maxLength = property.GetMaxLength();
                var columnType = property.GetColumnType();

                // Check if MaxLength is not set (null) or column type is nvarchar(max)/varchar(max)
                if (maxLength == null ||
                    columnType.Contains("max", StringComparison.OrdinalIgnoreCase) ||
                    columnType.Contains("nvarchar(max)", StringComparison.OrdinalIgnoreCase) ||
                    columnType.Contains("varchar(max)", StringComparison.OrdinalIgnoreCase))
                    violations.Add(
                        $"{entityType.ClrType.Name}.{property.Name} - " +
                        $"MaxLength: {(maxLength.HasValue ? maxLength.Value.ToString() : "NULL")}, " +
                        $"ColumnType: {columnType}");
            }

        violations.ShouldBeEmpty(
            $"The following string properties are configured as nvarchar(max) or varchar(max). " +
            $"All string properties MUST have explicit MaxLength defined:\n" +
            string.Join("\n", violations));
    }

    #endregion
}