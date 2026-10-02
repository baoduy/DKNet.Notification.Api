using System.Net.Http.Json;
using DKNet.EfCore.Specifications.Extensions;
using DKNet.EfCore.Specifications.Repositories;
using DKNet.Notification.App.TestSupport;
using DKNet.Notification.App.Tests.Integration.Support;
using DKNet.Notification.AppServices.ManualSample.V1;
using DKNet.Notification.AppServices.ManualSample.V1.Specs;
using DKNet.Notification.Share;

namespace DKNet.Notification.App.Tests.Integration.EndpointConfig;

/// <summary>
/// Re-homes the platform coverage <c>EndpointStampingAndVersioningTests</c> (deleted with the removed demo
/// entity's teardown) onto <c>PurchaseOrder</c>: the versioning gate governing route shape, and acting-user
/// attribution — sourced entirely from <c>[FromClaim]</c> + <c>AddContextualRequestPopulation</c>, never
/// stamped by the endpoint itself. <see cref="SharedConsts.SystemAccount" /> is no longer ever the resolved
/// <c>ByUser</c>: with <c>RequireAuthorization</c> off, the caller is the built-in demonstration provider
/// (<see cref="SharedConsts.DemoAccount" />), never an unauthenticated system fallback. An authenticated
/// caller whose token carries no <see cref="System.Security.Claims.ClaimTypes.Name" /> claim still gets an
/// unresolved (null) <c>ByUser</c> and the write is refused. Payload-spoofing resistance is already covered
/// by <c>PurchaseOrderSecurityTests</c>; this class does not duplicate it.
/// </summary>
public sealed class PurchaseOrderStampingAndVersioningTests
{
    private const string VersionedCreateUrl = "/v1/purchase-orders";
    private const string UnversionedCreateUrl = "/purchase-orders";

    #region Methods

    [Fact]
    public async Task VersioningOn_UnversionedRouteIsNotMapped()
    {
        using var fixture = new ApiFixture();
        await fixture.InitializeAsync();
        var client = fixture.CreateClient();

        using var response = await CreateAsync(client, UnversionedCreateUrl);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task VersioningOff_RouteCarriesNoVersionSegment_AndVersionedRouteIsGone()
    {
        using var fixture = new VersioningOffApiFixture();
        await fixture.InitializeAsync();
        var client = fixture.CreateClient();

        using var unversionedResponse = await CreateAsync(client, UnversionedCreateUrl);
        unversionedResponse.StatusCode.ShouldBe(HttpStatusCode.Created);

        using var versionedResponse = await CreateAsync(client, VersionedCreateUrl);
        versionedResponse.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    /// <summary>
    /// <c>RequireAuthorization</c> off and the built-in demonstration provider on → the caller is
    /// authenticated as the demonstration identity, so contextual population resolves <c>ByUser</c> to
    /// <see cref="SharedConsts.DemoAccount" />, never <see cref="SharedConsts.SystemAccount" /> — the endpoint
    /// stamps nothing itself.
    /// </summary>
    [Fact]
    public async Task DemoAuthentication_CreateIsAttributedToTheDemoIdentity()
    {
        using var fixture = new ApiFixture();
        await fixture.InitializeAsync();
        var client = fixture.CreateClient();

        using var response = await CreateAsync(client, VersionedCreateUrl);
        response.StatusCode.ShouldBe(HttpStatusCode.Created);

        var dto = await response.Content.ReadFromJsonAsync<PurchaseOrderDto>(SharedConsts.JsonSerializerOptions);
        dto!.CreatedBy.ShouldBe(SharedConsts.DemoAccount);
    }

    /// <summary>
    /// <c>RequireAuthorization</c> on but the caller's token carries no <c>ClaimTypes.Name</c> claim → the
    /// <c>[FromClaim]</c> resolver cannot resolve <c>ByUser</c>. There is no configured fallback for it to
    /// receive — <c>ByUser</c> holds its default (<see langword="null" />) instead, so the handler's own
    /// <c>string.IsNullOrEmpty(ByUser)</c> guard refuses the write. Distinct from the automated sample's
    /// <c>DataOwnerHook</c> path (see <c>ProductSecurityTests</c>).
    /// </summary>
    [Fact]
    public async Task AuthenticatedCallerWithNoNameClaim_CreateIsRefused_NeverAttributedToSystemAccount()
    {
        using var fixture = new AuthOnNoNameClaimApiFixture();
        await fixture.InitializeAsync();
        var client = fixture.CreateClient();

        using var response = await CreateAsync(client, VersionedCreateUrl);
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        using var scope = fixture.CreateScope();
        var repository = scope.ServiceProvider.GetRequiredService<IRepositorySpec>();
        var count = await repository.CountAsync(new SpecGetPurchaseOrder(), CancellationToken.None);
        count.ShouldBe(0, "a refused write must not create a row attributed to anyone, System included.");
    }

    private static async Task<HttpResponseMessage> CreateAsync(HttpClient client, string url)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = JsonContent.Create(new { customerName = "Acme Pte Ltd", amount = 100m })
        };
        request.Headers.Add("X-Idempotency-Key", Guid.NewGuid().ToString());

        return await client.SendAsync(request);
    }

    #endregion
}
