using DKNet.EfCore.Specifications.Extensions;
using DKNet.EfCore.Specifications.Repositories;
using DKNet.Notification.App.Tests.Integration.Support;
using DKNet.Notification.AppServices.ManualSample.V1.Actions;
using DKNet.Notification.AppServices.ManualSample.V1.Queries;
using DKNet.Notification.AppServices.ManualSample.V1.Specs;
using DKNet.Notification.Domains.Features.ManualSample.Entities;
using SlimMessageBus;

namespace DKNet.Notification.App.Tests.Integration.ManualSample.V1;

/// <summary>
/// Result-level integration coverage for the hand-written PurchaseOrder command/query handlers — asserted
/// on the <c>IResult</c>/<c>IResultBase</c> object returned by <see cref="IMessageBus.Send{TResponse}"/>,
/// not on HTTP status codes (that layer is BDD's — see CLAUDE.md's test-layering rule).
/// </summary>
public sealed class PurchaseOrderActionsIntegrationTests(ApiFixture fixture) : IClassFixture<ApiFixture>
{
    #region Create

    [Fact]
    public async Task Create_ShouldPersistOrder_AndReturnMatchingDto()
    {
        await fixture.ResetDatabaseAsync();
        using var scope = fixture.CreateScope();
        var bus = scope.ServiceProvider.GetRequiredService<IMessageBus>();
        var repository = scope.ServiceProvider.GetRequiredService<IRepositorySpec>();

        var result = await bus.Send(new CreatePurchaseOrderRequest
        {
            CustomerName = "Acme Pte Ltd",
            Amount = 250.00m,
            ByUser = "integration-test"
        });

        result.IsSuccess.ShouldBeTrue();
        result.Value!.CustomerName.ShouldBe("Acme Pte Ltd");
        result.Value.Amount.ShouldBe(250.00m);

        var created = await repository.FirstOrDefaultAsync(
            new SpecGetPurchaseOrder(byCustomerName: "Acme Pte Ltd"), CancellationToken.None);
        created.ShouldNotBeNull();
        created!.CreatedBy.ShouldBe("integration-test");
    }

    [Fact]
    public async Task Create_ShouldFail_WhenByUserIsMissing()
    {
        await fixture.ResetDatabaseAsync();
        using var scope = fixture.CreateScope();
        var bus = scope.ServiceProvider.GetRequiredService<IMessageBus>();

        var result = await bus.Send(new CreatePurchaseOrderRequest { CustomerName = "Acme", Amount = 10m });

        result.IsFailed.ShouldBeTrue();
    }

    #endregion

    #region Update

    [Fact]
    public async Task Update_ShouldChangeAmount_WhenOrderExists()
    {
        await fixture.ResetDatabaseAsync();
        using var scope = fixture.CreateScope();
        var bus = scope.ServiceProvider.GetRequiredService<IMessageBus>();
        var repository = scope.ServiceProvider.GetRequiredService<IRepositorySpec>();

        var order = new PurchaseOrder("Acme", 100m, "seed");
        await repository.AddAsync(order, CancellationToken.None);
        await repository.SaveChangesAsync(CancellationToken.None);

        var result = await bus.Send(new UpdatePurchaseOrderRequest
        {
            Id = order.Id,
            Amount = 999.99m,
            ByUser = "integration-test"
        });

        result.IsSuccess.ShouldBeTrue();
        var updated = await repository.FirstOrDefaultAsync(new SpecGetPurchaseOrder(order.Id), CancellationToken.None);
        updated!.Amount.ShouldBe(999.99m);
        updated.UpdatedBy.ShouldBe("integration-test");
    }

    [Fact]
    public async Task Update_ShouldFail_WhenOrderNotFound()
    {
        await fixture.ResetDatabaseAsync();
        using var scope = fixture.CreateScope();
        var bus = scope.ServiceProvider.GetRequiredService<IMessageBus>();

        var result = await bus.Send(new UpdatePurchaseOrderRequest
        {
            Id = Guid.NewGuid(),
            Amount = 50m,
            ByUser = "integration-test"
        });

        result.IsFailed.ShouldBeTrue();
    }

    [Fact]
    public async Task Update_ShouldFail_WhenByUserIsMissing()
    {
        await fixture.ResetDatabaseAsync();
        using var scope = fixture.CreateScope();
        var bus = scope.ServiceProvider.GetRequiredService<IMessageBus>();
        var repository = scope.ServiceProvider.GetRequiredService<IRepositorySpec>();

        var order = new PurchaseOrder("Acme", 100m, "seed");
        await repository.AddAsync(order, CancellationToken.None);
        await repository.SaveChangesAsync(CancellationToken.None);

        var result = await bus.Send(new UpdatePurchaseOrderRequest { Id = order.Id, Amount = 50m });

        result.IsFailed.ShouldBeTrue();
    }

    #endregion

    #region Cancel

    [Fact]
    public async Task Cancel_ShouldSucceedOnce_ThenFail_WhenAlreadyCancelled()
    {
        await fixture.ResetDatabaseAsync();
        using var scope = fixture.CreateScope();
        var bus = scope.ServiceProvider.GetRequiredService<IMessageBus>();
        var repository = scope.ServiceProvider.GetRequiredService<IRepositorySpec>();

        var order = new PurchaseOrder("Acme", 100m, "seed");
        await repository.AddAsync(order, CancellationToken.None);
        await repository.SaveChangesAsync(CancellationToken.None);

        var first = await bus.Send(new CancelPurchaseOrderRequest { Id = order.Id, ByUser = "integration-test" });
        first.IsSuccess.ShouldBeTrue();

        var second = await bus.Send(new CancelPurchaseOrderRequest { Id = order.Id, ByUser = "integration-test" });
        second.IsFailed.ShouldBeTrue();
        second.Errors.Select(e => e.Message).ShouldContain(m => m.Contains("already cancelled", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Cancel_ShouldFail_WhenOrderNotFound()
    {
        await fixture.ResetDatabaseAsync();
        using var scope = fixture.CreateScope();
        var bus = scope.ServiceProvider.GetRequiredService<IMessageBus>();

        var result = await bus.Send(new CancelPurchaseOrderRequest { Id = Guid.NewGuid(), ByUser = "integration-test" });

        result.IsFailed.ShouldBeTrue();
    }

    [Fact]
    public async Task Cancel_ShouldFail_WhenByUserIsMissing()
    {
        await fixture.ResetDatabaseAsync();
        using var scope = fixture.CreateScope();
        var bus = scope.ServiceProvider.GetRequiredService<IMessageBus>();
        var repository = scope.ServiceProvider.GetRequiredService<IRepositorySpec>();

        var order = new PurchaseOrder("Acme", 100m, "seed");
        await repository.AddAsync(order, CancellationToken.None);
        await repository.SaveChangesAsync(CancellationToken.None);

        var result = await bus.Send(new CancelPurchaseOrderRequest { Id = order.Id });

        result.IsFailed.ShouldBeTrue();
    }

    #endregion

    #region Delete

    [Fact]
    public async Task Delete_ShouldRemoveOrder()
    {
        await fixture.ResetDatabaseAsync();
        using var scope = fixture.CreateScope();
        var bus = scope.ServiceProvider.GetRequiredService<IMessageBus>();
        var repository = scope.ServiceProvider.GetRequiredService<IRepositorySpec>();

        var order = new PurchaseOrder("Acme", 100m, "seed");
        await repository.AddAsync(order, CancellationToken.None);
        await repository.SaveChangesAsync(CancellationToken.None);

        var result = await bus.Send(new DeletePurchaseOrderRequest { Id = order.Id, ByUser = "integration-test" });

        result.IsSuccess.ShouldBeTrue();
        var deleted = await repository.FirstOrDefaultAsync(new SpecGetPurchaseOrder(order.Id), CancellationToken.None);
        deleted.ShouldBeNull();
    }

    [Fact]
    public async Task Delete_ShouldFail_WhenOrderNotFound()
    {
        await fixture.ResetDatabaseAsync();
        using var scope = fixture.CreateScope();
        var bus = scope.ServiceProvider.GetRequiredService<IMessageBus>();

        var result = await bus.Send(new DeletePurchaseOrderRequest { Id = Guid.NewGuid(), ByUser = "integration-test" });

        result.IsFailed.ShouldBeTrue();
    }

    [Fact]
    public async Task Delete_ShouldFail_WhenByUserIsMissing()
    {
        await fixture.ResetDatabaseAsync();
        using var scope = fixture.CreateScope();
        var bus = scope.ServiceProvider.GetRequiredService<IMessageBus>();
        var repository = scope.ServiceProvider.GetRequiredService<IRepositorySpec>();

        var order = new PurchaseOrder("Acme", 100m, "seed");
        await repository.AddAsync(order, CancellationToken.None);
        await repository.SaveChangesAsync(CancellationToken.None);

        var result = await bus.Send(new DeletePurchaseOrderRequest { Id = order.Id });

        result.IsFailed.ShouldBeTrue();
    }

    #endregion

    #region Queries

    [Fact]
    public async Task GetById_ShouldReturnNull_WhenNotFound()
    {
        await fixture.ResetDatabaseAsync();
        using var scope = fixture.CreateScope();
        var bus = scope.ServiceProvider.GetRequiredService<IMessageBus>();

        var dto = await bus.Send(new GetPurchaseOrderByIdQuery { Id = Guid.NewGuid() });

        dto.ShouldBeNull();
    }

    [Fact]
    public async Task List_ShouldFilterByCustomerName()
    {
        await fixture.ResetDatabaseAsync();
        using var scope = fixture.CreateScope();
        var bus = scope.ServiceProvider.GetRequiredService<IMessageBus>();
        var repository = scope.ServiceProvider.GetRequiredService<IRepositorySpec>();

        await repository.AddAsync(new PurchaseOrder("Acme", 10m, "seed"), CancellationToken.None);
        await repository.AddAsync(new PurchaseOrder("Globex", 20m, "seed"), CancellationToken.None);
        await repository.SaveChangesAsync(CancellationToken.None);

        var page = await bus.Send(new ListPurchaseOrdersQuery { CustomerName = "Acme" });
        var results = page.ToList();

        results.Count.ShouldBe(1);
        results.Single().CustomerName.ShouldBe("Acme");
    }

    #endregion
}
