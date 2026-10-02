using DKNet.Notification.AppServices.ManualSample.V1.Actions;

namespace DKNet.Notification.App.Tests.Unit.ManualSample;

public class PurchaseOrderValidatorsTests
{
    private readonly CreatePurchaseOrderCommandValidator _createValidator = new();
    private readonly UpdatePurchaseOrderCommandValidator _updateValidator = new();

    #region Create

    [Fact]
    public void CreateValidator_ShouldFail_WhenCustomerNameIsBlank()
    {
        var result = _createValidator.Validate(new CreatePurchaseOrderRequest { CustomerName = "", Amount = 10m });

        result.IsValid.ShouldBeFalse();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public void CreateValidator_ShouldFail_WhenAmountIsNotPositive(decimal amount)
    {
        var result = _createValidator.Validate(new CreatePurchaseOrderRequest { CustomerName = "Acme", Amount = amount });

        result.IsValid.ShouldBeFalse();
    }

    [Fact]
    public void CreateValidator_ShouldPass_WhenRequestIsValid()
    {
        var result = _createValidator.Validate(new CreatePurchaseOrderRequest { CustomerName = "Acme", Amount = 10m });

        result.IsValid.ShouldBeTrue();
    }

    #endregion

    #region Update

    [Fact]
    public void UpdateValidator_ShouldFail_WhenAmountIsNotPositive()
    {
        var result = _updateValidator.Validate(new UpdatePurchaseOrderRequest { Amount = 0m });

        result.IsValid.ShouldBeFalse();
    }

    [Fact]
    public void UpdateValidator_ShouldPass_WhenAmountIsPositive()
    {
        var result = _updateValidator.Validate(new UpdatePurchaseOrderRequest { Amount = 10m });

        result.IsValid.ShouldBeTrue();
    }

    [Fact]
    public void UpdateValidator_ShouldNotRejectEmptyId()
    {
        // Regression guard for the DRK-714 bug: Id comes from the route, not the body, so the validator
        // must not fail on an empty Id — an unknown/empty id 404s from the handler's spec lookup instead.
        var result = _updateValidator.Validate(new UpdatePurchaseOrderRequest { Id = Guid.Empty, Amount = 10m });

        result.IsValid.ShouldBeTrue();
    }

    #endregion
}
