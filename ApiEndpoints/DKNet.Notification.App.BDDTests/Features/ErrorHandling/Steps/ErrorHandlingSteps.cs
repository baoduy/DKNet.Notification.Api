using System.Net.Http.Json;
using DKNet.Notification.App.BDDTests.Features.PurchaseOrders.Steps;

namespace DKNet.Notification.App.BDDTests.Features.ErrorHandling.Steps;

[Binding]
public sealed class ErrorHandlingSteps(HttpClient client, ScenarioState state, PurchaseOrderSteps purchaseOrderSteps)
{
    #region Given

    [Given(@"a service generated from the `DKNet\.Templates` starter registers the standard error setting and runs in production")]
    public void GivenAServiceRegistersTheStandardErrorSettingAndRunsInProduction()
    {
        // Program.cs calls AddFluentValidationConfig() unconditionally on every boot, and TestApiFactoryBase
        // already runs this host in the "Testing" environment — non-Development, which is what R3 needs.
    }

    [Given(@"a service generated from the `DKNet\.Templates` starter registers a setting that answers 409 for a failure marked ""precondition""")]
    public void GivenAServiceRegistersAPreconditionSetting()
    {
        // Same registered app as above — FluentValidationConfig's existing StatusCode rule already answers
        // 409 for any failure whose code starts with PreconditionCodes.Prefix.
    }

    #endregion

    #region When

    [When("storefront sends a request that raises an unexpected error")]
    public async Task WhenStorefrontSendsARequestThatRaisesAnUnexpectedError()
    {
        // Creating a purchase order maps its entity to the response DTO in the same request — the
        // trigger-aware IMapper throws there, a genuine unhandled exception from inside the handler an
        // existing route already calls.
        using var request = new HttpRequestMessage(HttpMethod.Post, "/v1/purchase-orders")
        {
            Content = JsonContent.Create(new
            {
                customerName = UnexpectedErrorTriggerMapper.TriggerValue,
                amount = 10.00m
            })
        };
        request.Headers.Add("X-Idempotency-Key", Guid.NewGuid().ToString());

        state.Response = await client.SendAsync(request);
        state.ResponseBody = await state.Response.Content.ReadAsStringAsync();
    }

    [When(@"an endpoint of that service answers a command failure marked ""precondition""")]
    public async Task WhenAnEndpointAnswersACommandFailureMarkedPrecondition()
    {
        await purchaseOrderSteps.GivenAPurchaseOrderExists("Precondition Co", 10.00m);
        await purchaseOrderSteps.WhenICancelThatPurchaseOrder();
        await purchaseOrderSteps.WhenICancelThatPurchaseOrder();
    }

    #endregion

    #region Then

    [Then(@"the response body carries the title ""Error"", a status, a type, a trace identifier and an error list in the standard shape")]
    public void ThenTheResponseBodyCarriesTheStandardErrorShape()
    {
        state.ResponseBody.ShouldNotBeNullOrEmpty();
        using var doc = JsonDocument.Parse(state.ResponseBody!);
        var root = doc.RootElement;

        root.GetProperty("title").GetString().ShouldBe("Error");

        root.TryGetProperty("status", out var status).ShouldBeTrue();
        status.ValueKind.ShouldBe(JsonValueKind.Number);

        root.TryGetProperty("type", out var type).ShouldBeTrue();
        type.GetString().ShouldNotBeNullOrEmpty();

        root.TryGetProperty("traceId", out var traceId).ShouldBeTrue();
        traceId.GetString().ShouldNotBeNullOrEmpty();

        root.TryGetProperty("errors", out var errors).ShouldBeTrue();
        errors.ValueKind.ShouldBe(JsonValueKind.Array);
        errors.GetArrayLength().ShouldBeGreaterThan(0);

        errors[0].TryGetProperty("message", out var message).ShouldBeTrue();
        message.GetString().ShouldNotBeNullOrEmpty();
    }

    #endregion
}
