using DKNet.EfCore.Specifications.Repositories;
using DKNet.Notification.App.Tests.Integration.Support;
using DKNet.Notification.AppServices.Share.Generics;
using DKNet.Notification.Domains.Features.ManualSample.Entities;

namespace DKNet.Notification.App.Tests.Integration.StatusCounts;

/// <summary>
/// DRK-500 "Also verify": <c>MapGetStatusCounts</c> was relocated to its own template-local file
/// (status-count endpoints stay template-local per §4) while everything else moved into the package. No
/// current <see cref="DKNet.Notification.Api.ApiEndpoints"/> config calls it, so there is no HTTP route to hit through
/// the real host — this exercises the exact <c>GetStatusCounts</c> call its handler delegates to, against the
/// same fully-wired DI/EF stack (row-level filters included) the real app uses, proving the relocation didn't
/// break the underlying query. Re-homed onto <c>PurchaseOrder</c>/<c>PurchaseOrderStatus</c> (the removed
/// demo entity this test class originally used no longer exists).
/// </summary>
public sealed class StatusCountsEndpointMapperExtensionsTests(ApiFixture fixture) : IClassFixture<ApiFixture>
{
    [Fact]
    public async Task GetStatusCounts_ShouldReturnAllEnumValues_WithRealCountsForSeededOnes()
    {
        await fixture.ResetDatabaseAsync();

        using var scope = fixture.CreateScope();
        var repository = scope.ServiceProvider.GetRequiredService<IRepositorySpec>();

        await repository.AddAsync(new PurchaseOrder("Acme Pte Ltd", 100m, "seed"), CancellationToken.None);
        await repository.SaveChangesAsync(CancellationToken.None);

        var results = await repository.GetStatusCounts<PurchaseOrder>(
            new StatusPropertyInfo(nameof(PurchaseOrder.Status), typeof(PurchaseOrderStatus)),
            new GenericStatusCountsParameters());

        results.ShouldContain(r => r.Type == nameof(PurchaseOrderStatus) && r.Status == "PLACED" && r.Count == 1);
        results.ShouldContain(r => r.Type == nameof(PurchaseOrderStatus) && r.Status == "DRAFT" && r.Count == 0);
        results.ShouldContain(r => r.Type == nameof(PurchaseOrderStatus) && r.Status == "CANCELLED" && r.Count == 0);
    }

    /// <summary>
    /// DRK-506 §5 "Status counts over the full history": DRK-521's Build stage removed the implicit 30-day
    /// window (<see cref="GenericStatusCountsParameters.From"/>/<see cref="GenericStatusCountsParameters.To"/> now
    /// apply only when supplied), so a call with no date parameters must count records regardless of age.
    /// </summary>
    [Fact]
    public async Task GetStatusCounts_ShouldCountRecordsOfAnyAge_WhenNoDateRangeGiven()
    {
        await fixture.ResetDatabaseAsync();

        using var scope = fixture.CreateScope();
        var repository = scope.ServiceProvider.GetRequiredService<IRepositorySpec>();

        var recentOrder = new PurchaseOrder("Recent Customer", 100m, "seed");
        var historicalOrder = new PurchaseOrder("Historical Customer", 100m, "seed");

        await repository.AddAsync(recentOrder, CancellationToken.None);
        await repository.AddAsync(historicalOrder, CancellationToken.None);
        repository.Entry(historicalOrder).Property(x => x.CreatedOn).CurrentValue =
            DateTimeOffset.UtcNow.AddDays(-90);
        await repository.SaveChangesAsync(CancellationToken.None);

        var results = await repository.GetStatusCounts<PurchaseOrder>(
            new StatusPropertyInfo(nameof(PurchaseOrder.Status), typeof(PurchaseOrderStatus)),
            new GenericStatusCountsParameters());

        results.ShouldContain(r =>
            r.Type == nameof(PurchaseOrderStatus) && r.Status == "PLACED" && r.Count == 2,
            "a 90-day-old record must still be counted when no From/To is supplied.");
    }

    /// <summary>
    /// DRK-506 §5 "Status counts over the full history": an explicit date range must still narrow the result
    /// even though the full-history default (no bound) now applies when dates are absent.
    /// </summary>
    [Fact]
    public async Task GetStatusCounts_ShouldExcludeRecordsOutsideRange_WhenExplicitDateRangeGiven()
    {
        await fixture.ResetDatabaseAsync();

        using var scope = fixture.CreateScope();
        var repository = scope.ServiceProvider.GetRequiredService<IRepositorySpec>();

        var recentOrder = new PurchaseOrder("Recent Customer", 100m, "seed");
        var historicalOrder = new PurchaseOrder("Historical Customer", 100m, "seed");

        await repository.AddAsync(recentOrder, CancellationToken.None);
        await repository.AddAsync(historicalOrder, CancellationToken.None);
        repository.Entry(historicalOrder).Property(x => x.CreatedOn).CurrentValue =
            DateTimeOffset.UtcNow.AddDays(-90);
        await repository.SaveChangesAsync(CancellationToken.None);

        var results = await repository.GetStatusCounts<PurchaseOrder>(
            new StatusPropertyInfo(nameof(PurchaseOrder.Status), typeof(PurchaseOrderStatus)),
            new GenericStatusCountsParameters
            {
                From = DateTimeOffset.UtcNow.AddDays(-7),
                To = DateTimeOffset.UtcNow.AddDays(1)
            });

        results.ShouldContain(r =>
            r.Type == nameof(PurchaseOrderStatus) && r.Status == "PLACED" && r.Count == 1,
            "the 90-day-old record must be excluded once an explicit From/To range narrows the window to the last 7 days.");
    }
}
