using DKNet.Notification.App.Tests.Integration.Support;

namespace DKNet.Notification.App.Tests.Integration.AutomatedSample.V1;

/// <summary>
/// DRK-1410: <c>DeleteProductRequestValidator</c> must pass an unknown id — refusing it would hide the
/// route's own 404 behind a 409. BDD covers the "still for sale" (409) and "discontinued" (204) branches
/// of the same rule end to end; this is the one branch — the target not existing at all — that has no
/// user-facing scenario of its own.
/// </summary>
public sealed class ProductPreconditionTests(AuthOnApiFixture fixture) : IClassFixture<AuthOnApiFixture>
{
    [Fact]
    public async Task DeletingAnUnknownProduct_PassesThePreconditionAndStillAnswers404()
    {
        await fixture.ResetDatabaseAsync();
        var client = fixture.CreateClient();

        using var request = new HttpRequestMessage(HttpMethod.Delete, $"/v1/products/{Guid.NewGuid()}");
        request.Headers.Add("X-Test-Scopes", "products.write");
        using var response = await client.SendAsync(request);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }
}
