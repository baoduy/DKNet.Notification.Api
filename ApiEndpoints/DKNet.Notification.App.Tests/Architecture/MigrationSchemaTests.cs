using Microsoft.EntityFrameworkCore;
using DKNet.Notification.Domains.Features.AutomatedSample.Entities;
using DKNet.Notification.Infra.Contexts;

namespace DKNet.Notification.App.Tests.Architecture;

public class MigrationSchemaTests
{
    [Fact]
    public void EfCoreModel_ShouldDeclareProductNameUnique()
    {
        // DRK-1410: the uniqueness rule is a check backed by a real guarantee — the database constraint,
        // not the validator, is what actually keeps a product name unique.
        using var dbContext = new DbContextFactory().CreateDbContext([]);
        var entityType = dbContext.Model.FindEntityType(typeof(Product));
        entityType.ShouldNotBeNull();

        var nameIndex = entityType!.GetIndexes()
            .SingleOrDefault(i => i.Properties.Count == 1 && i.Properties[0].Name == nameof(Product.Name));

        nameIndex.ShouldNotBeNull("Product.Name must have an index declared in the EF Core model.");
        nameIndex!.IsUnique.ShouldBeTrue("Product.Name's index must be unique.");
    }


    [Fact]
    public void Migration_ShouldCreate_SeqSchema()
    {
        using var dbContext = new DbContextFactory().CreateDbContext([]);
        var sequences = dbContext.Model.GetSequences()
            .Select(s => s.Schema)
            .Distinct()
            .ToHashSet();
        sequences.ShouldContain("seq");
    }

    [Fact]
    public void Migration_ShouldHave_OnlySeqMembership_NotSeqNone()
    {
        using var dbContext = new DbContextFactory().CreateDbContext([]);
        var sequenceNames = dbContext.Model.GetSequences()
            .Select(s => s.Name)
            .ToArray();

        sequenceNames.ShouldContain("Seq_Membership");
        sequenceNames.Any(s => s.Contains("None", StringComparison.OrdinalIgnoreCase))
            .ShouldBeFalse();
    }

    [Fact]
    public void EfCoreModel_ShouldTarget_PostgreSqlProvider()
    {
        using var dbContext = new DbContextFactory().CreateDbContext([]);
        var providerName = dbContext.Database.ProviderName;
        providerName.ShouldNotBeNullOrWhiteSpace();
        providerName!.Contains("SqlServer", StringComparison.OrdinalIgnoreCase).ShouldBeFalse();
    }

    [Fact]
    public void AppHost_ShouldReference_PostgreSqlNotSqlServer()
    {
        var srcDir = Path.GetFullPath(
            Path.Combine(AppContext.BaseDirectory,
                "../../../../../ApiEndpoints/DKNet.Notification.AppHost"));

        var csprojPath = Path.Combine(srcDir, "DKNet.Notification.AppHost.csproj");
        File.Exists(csprojPath).ShouldBeTrue();

        var content = File.ReadAllText(csprojPath);

        content.Contains("Aspire.Hosting.PostgreSQL", StringComparison.Ordinal).ShouldBeTrue();
        content.Contains("Aspire.Hosting.SqlServer", StringComparison.OrdinalIgnoreCase).ShouldBeFalse();
    }

    [Fact]
    public void InfraCsproj_ShouldReference_NpgsqlNotSqlServer()
    {
        var srcDir = Path.GetFullPath(
            Path.Combine(AppContext.BaseDirectory,
                "../../../../../ApiEndpoints/DKNet.Notification.Infra"));

        var csprojPath = Path.Combine(srcDir, "DKNet.Notification.Infra.csproj");
        File.Exists(csprojPath).ShouldBeTrue();

        var content = File.ReadAllText(csprojPath);

        content.Contains("Npgsql.EntityFrameworkCore.PostgreSQL", StringComparison.Ordinal).ShouldBeTrue();
        content.Contains("Microsoft.EntityFrameworkCore.SqlServer", StringComparison.OrdinalIgnoreCase).ShouldBeFalse();
    }
}