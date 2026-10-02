using System.Diagnostics.CodeAnalysis;
using Bogus;
using DKNet.EfCore.DataAuthorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using DKNet.Notification.Domains.Features.AutomatedSample.Entities;
using DKNet.Notification.Domains.Features.ManualSample.Entities;
using DKNet.Notification.Domains.Share;
using DKNet.Notification.Infra.Extensions;
using DKNet.Notification.Share;

namespace DKNet.Notification.AppHost.SampleData;

/// <summary>
/// Populates the two sample entities with randomised dev data after the AppHost's resources are up.
/// Deliberately never fails the host — every failure is caught, logged, and swallowed.
/// </summary>
[ExcludeFromCodeCoverage]
internal static class SampleDataGenerator
{
    #region Constants

    // PurchaseOrderStaticData seeds exactly 3 reference orders as part of the startup migration.
    private const int StaticPurchaseOrderSeedCount = 3;

    private const int ChunkSize = 1000;

    /// <summary>
    /// Name and reference code of the single seeded product that demonstrates both role-gated
    /// <c>[SensitiveData]</c> properties (<c>SupplierCostPrice</c>, <c>SupplierReferenceCode</c>) together —
    /// every other generated product carries neither. Fixed and grep-distinct so a developer calling
    /// <c>GET /v1/products</c> against the Aspire AppHost can find it on sight.
    /// </summary>
    internal const string DemonstrationProductName = "Demo-Product-With-Supplier-Data";

    internal const decimal DemonstrationProductSupplierCostPrice = 42.50m;
    internal const string DemonstrationProductSupplierReferenceCode = "DEMO-SUPPLIER-REF";
    private static readonly TimeSpan SchemaWaitDeadline = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan SchemaPollInterval = TimeSpan.FromSeconds(1);

    #endregion

    #region Methods

    public static async Task RunAsync(
        string connectionString,
        int recordsPerEntity,
        ILogger logger,
        CancellationToken cancellationToken = default)
    {
        if (recordsPerEntity <= 0)
        {
            logger.LogInformation(
                "Sample-data generation disabled (SampleData:RecordsPerEntity <= 0) — skipping.");
            return;
        }

        try
        {
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();

            await using var db = new SampleDataDbContext(
                new DbContextOptionsBuilder<SampleDataDbContext>()
                    .UseNpgsql(connectionString, o => o.MaxBatchSize(1000))
                    .UseAutoConfigModel([typeof(InfraSetup).Assembly, typeof(Sequences).Assembly])
                    .Options);
            db.ChangeTracker.AutoDetectChangesEnabled = false;

            var (schemaReady, schemaPollFailure) = await WaitForSchemaAsync(db, logger, cancellationToken);
            if (!schemaReady)
            {
                logger.LogWarning(
                    "Sample-data generation skipped: database schema is absent after a {DeadlineSeconds}s wait — " +
                    "either the API's startup migration (FeatureManagement:RunDbMigrationWhenAppStart) is disabled, " +
                    "or the schema query kept failing for another reason. Last observed failure: {LastPollFailure}",
                    SchemaWaitDeadline.TotalSeconds,
                    schemaPollFailure?.Message ?? "none — the query never returned before the deadline");
                return;
            }

            var productsWritten = await GenerateProductsAsync(db, recordsPerEntity, logger, cancellationToken);
            var ordersWritten = await GeneratePurchaseOrdersAsync(db, recordsPerEntity, logger, cancellationToken);

            logger.LogInformation(
                "Sample-data generation complete: {Products} products, {Orders} purchase orders written in {ElapsedMs} ms.",
                productsWritten, ordersWritten, stopwatch.ElapsedMilliseconds);
        }
        catch (OperationCanceledException)
        {
            logger.LogInformation("Sample-data generation cancelled.");
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Sample-data generation failed and was skipped.");
        }
    }

    /// <summary>
    /// Polls until the migration-created <c>sample."Products"</c> table is queryable, or the deadline passes.
    /// A query failure (container still booting, schema not migrated yet) is treated as "not ready", not an
    /// error — but the last such failure is kept (not discarded) so the caller can tell a genuinely absent
    /// schema apart from, say, a bad password or an unreachable host, and the wait itself is logged once so
    /// it is visible in the logs a developer is already reading instead of a full silent minute.
    /// </summary>
    private static async Task<(bool Ready, Exception? LastFailure)> WaitForSchemaAsync(
        SampleDataDbContext db, ILogger logger, CancellationToken cancellationToken)
    {
        var deadline = DateTimeOffset.UtcNow + SchemaWaitDeadline;
        Exception? lastFailure = null;
        var loggedWaiting = false;

        while (DateTimeOffset.UtcNow < deadline)
        {
            try
            {
                await db.Set<Product>().AnyAsync(cancellationToken);
                return (true, null);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                lastFailure = ex;
                if (!loggedWaiting)
                {
                    loggedWaiting = true;
                    logger.LogInformation(
                        "Sample-data generation is waiting for the database schema to become queryable (up to {DeadlineSeconds}s)...",
                        SchemaWaitDeadline.TotalSeconds);
                }
            }

            await Task.Delay(SchemaPollInterval, cancellationToken);
        }

        return (false, lastFailure);
    }

    private static async Task<int> GenerateProductsAsync(
        SampleDataDbContext db, int count, ILogger logger, CancellationToken cancellationToken)
    {
        if (await db.Set<Product>().CountAsync(cancellationToken) > 0)
        {
            logger.LogInformation(
                "Skipping product generation: existing rows found — not accumulating into a retained database.");
            return 0;
        }

        var faker = new Faker();
        var usedNames = new HashSet<string>(StringComparer.Ordinal) { DemonstrationProductName };
        var now = DateTimeOffset.UtcNow;

        var demonstrationProduct = new Product(
            DemonstrationProductName, faker.Random.Decimal(0.01m, 10_000m), DemonstrationProductSupplierCostPrice);
        demonstrationProduct.AssignSupplierReference(DemonstrationProductSupplierReferenceCode);
        db.Add(demonstrationProduct);
        StampAuditProperties(db, demonstrationProduct, now);
        await db.SaveChangesAsync(cancellationToken);
        db.ChangeTracker.Clear();

        foreach (var chunkSize in ChunkSizes(count - 1))
        {
            var products = new List<Product>(chunkSize);
            for (var i = 0; i < chunkSize; i++)
                products.Add(new Product(NextUniqueProductName(faker, usedNames), faker.Random.Decimal(0.01m, 10_000m)));

            db.AddRange(products);
            foreach (var product in products) StampAuditProperties(db, product, now);

            await db.SaveChangesAsync(cancellationToken);
            db.ChangeTracker.Clear();
        }

        return count;
    }

    private static void StampAuditProperties(SampleDataDbContext db, Product product, DateTimeOffset now)
    {
        var entry = db.Entry(product);
        entry.Property(nameof(Product.CreatedBy)).CurrentValue = SharedConsts.SystemAccount;
        entry.Property(nameof(Product.CreatedOn)).CurrentValue = now;
        entry.Property(nameof(Product.OwnedBy)).CurrentValue = SharedConsts.SystemAccount;
    }

    private static async Task<int> GeneratePurchaseOrdersAsync(
        SampleDataDbContext db, int count, ILogger logger, CancellationToken cancellationToken)
    {
        if (await db.Set<PurchaseOrder>().CountAsync(cancellationToken) > StaticPurchaseOrderSeedCount)
        {
            logger.LogInformation(
                "Skipping purchase-order generation: existing rows found — not accumulating into a retained database.");
            return 0;
        }

        var faker = new Faker();

        foreach (var chunkSize in ChunkSizes(count))
        {
            var orders = new List<PurchaseOrder>(chunkSize);
            for (var i = 0; i < chunkSize; i++)
            {
                var customerName = Truncate(faker.Company.CompanyName(), 200);
                orders.Add(new PurchaseOrder(customerName, faker.Random.Decimal(0.01m, 100_000m), SharedConsts.SystemAccount));
            }

            db.AddRange(orders);
            await db.SaveChangesAsync(cancellationToken);
            db.ChangeTracker.Clear();
        }

        return count;
    }

    private static IEnumerable<int> ChunkSizes(int total)
    {
        var remaining = total;
        while (remaining > 0)
        {
            var size = Math.Min(ChunkSize, remaining);
            yield return size;
            remaining -= size;
        }
    }

    /// <summary>
    /// Bogus' Commerce.ProductName() collides well before 10 000 draws — dedupe against the batch and
    /// disambiguate with a random token before falling back to a guaranteed-unique id.
    /// </summary>
    private static string NextUniqueProductName(Faker faker, HashSet<string> usedNames)
    {
        for (var attempt = 0; attempt < 20; attempt++)
        {
            var candidate = attempt == 0
                ? faker.Commerce.ProductName()
                : $"{faker.Commerce.ProductName()} {faker.Random.AlphaNumeric(6)}";

            candidate = Truncate(candidate, 150);
            if (usedNames.Add(candidate)) return candidate;
        }

        var fallback = Guid.NewGuid().ToString("N")[..20];
        usedNames.Add(fallback);
        return fallback;
    }

    private static string Truncate(string value, int maxLength) =>
        value.Length > maxLength ? value[..maxLength] : value;

    #endregion

    /// <summary>
    /// Bare EF Core context used only to write sample data outside DI. It is never given
    /// <c>UseAutoDataSeeding</c>, so no domain-event hook is registered against it — generated rows publish
    /// no notification. It implements <c>IDataOwnerDbContext</c> with <c>IsUnrestrictedAccess</c> so that
    /// <see cref="DKNet.EfCore.DataAuthorization"/>'s global query filter — auto-attached to any scanned
    /// <see cref="IOwnedBy"/> entity such as <c>Product</c>, for any DbContext — sees every row instead of
    /// throwing for a context that can't supply real accessible keys.
    /// </summary>
    private sealed class SampleDataDbContext(DbContextOptions<SampleDataDbContext> options)
        : DbContext(options), IDataOwnerDbContext
    {
        public IEnumerable<string> AccessibleKeys => [];

        public bool IsUnrestrictedAccess => true;
    }
}
