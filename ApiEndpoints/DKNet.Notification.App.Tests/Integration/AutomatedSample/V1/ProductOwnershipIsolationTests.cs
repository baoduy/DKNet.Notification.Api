using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using DKNet.Notification.App.TestSupport;
using DKNet.Notification.App.Tests.Integration.Support;
using DKNet.Notification.AppServices.AutomatedSample.V1;
using DKNet.Notification.Domains.Features.AutomatedSample.Entities;
using DKNet.Notification.Infra.Contexts;

namespace DKNet.Notification.App.Tests.Integration.AutomatedSample.V1;

/// <summary>
/// Proves the row-level authorization boundary end to end through <c>DataOwnerHook</c> (write) and
/// <c>DataOwnerAuthQuery</c> (read filter) — not just that <c>PrincipalProvider</c> resolves distinct keys
/// (see <c>Unit.Configs.PrincipalProviderTests</c>). Two authenticated callers share one host/database via
/// <see cref="MultiSubjectAuthHandler"/>, so a collapse onto a shared ownership key would show up here as one
/// caller reading the other's row — not merely as an equal string somewhere.
/// </summary>
public sealed class ProductOwnershipIsolationTests(AuthOnMultiSubjectApiFixture fixture)
    : IClassFixture<AuthOnMultiSubjectApiFixture>
{
    [Fact]
    public async Task DifferentNonGuidSubjects_GetDistinctOwnershipKeys_AndNeitherCanReadTheOthersProduct()
    {
        await fixture.ResetDatabaseAsync();
        var client = fixture.CreateClient();

        var productA = await CreateProductAsync(client, "opaque-subject-a", oid: null, name: "A's Widget");
        var productB = await CreateProductAsync(client, "opaque-subject-b", oid: null, name: "B's Widget");

        productA.CreatedBy.ShouldBe("opaque-subject-a");
        productB.CreatedBy.ShouldBe("opaque-subject-b");
        productA.CreatedBy.ShouldNotBe(productB.CreatedBy);
        productA.CreatedBy.ShouldNotBe(Guid.Empty.ToString());
        productB.CreatedBy.ShouldNotBe(Guid.Empty.ToString());

        // Caller B must not be able to read caller A's product — the query filter denies it, not a 403.
        using var crossReadRequest = new HttpRequestMessage(HttpMethod.Get, $"/v1/products/{productA.Id}");
        crossReadRequest.Headers.Add(MultiSubjectAuthHandler.SubjectHeaderName, "opaque-subject-b");
        using var crossReadResponse = await client.SendAsync(crossReadRequest);
        crossReadResponse.StatusCode.ShouldBe(HttpStatusCode.NotFound);

        // The flip side of the same assertion: this must be a real per-caller filter, not "everyone denied" —
        // caller A must still be able to read their own row back.
        using var ownReadRequest = new HttpRequestMessage(HttpMethod.Get, $"/v1/products/{productA.Id}");
        ownReadRequest.Headers.Add(MultiSubjectAuthHandler.SubjectHeaderName, "opaque-subject-a");
        using var ownReadResponse = await client.SendAsync(ownReadRequest);
        ownReadResponse.StatusCode.ShouldBe(HttpStatusCode.OK);

        // And caller B's own list must never surface caller A's row.
        using var listRequest = new HttpRequestMessage(HttpMethod.Get, "/v1/products?pageSize=100");
        listRequest.Headers.Add(MultiSubjectAuthHandler.SubjectHeaderName, "opaque-subject-b");
        using var listResponse = await client.SendAsync(listRequest);
        var envelope = await listResponse.Content.ReadFromJsonAsync<ProductListEnvelope>();
        envelope!.Items.ShouldContain(p => p.Id == productB.Id);
        envelope.Items.ShouldNotContain(p => p.Id == productA.Id);

        // The read filter keys off OwnedBy, not CreatedBy — the two must never disagree. Bypass the filter
        // with IgnoreQueryFilters() (a raw EF Core query flag, unaffected by DataOwnerAuthQuery.IsIgnorable)
        // to inspect both columns directly; a row stamped with one and not the other is a silent hole the
        // HTTP-level assertions above cannot see (both would just look "denied to everyone").
        using var scope = fixture.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<CoreDbContext>();
        var storedA = await dbContext.Set<Product>().IgnoreQueryFilters()
            .SingleAsync(p => p.Id == productA.Id);
        var storedB = await dbContext.Set<Product>().IgnoreQueryFilters()
            .SingleAsync(p => p.Id == productB.Id);
        storedA.OwnedBy.ShouldBe(productA.CreatedBy);
        storedB.OwnedBy.ShouldBe(productB.CreatedBy);
    }

    [Fact]
    public async Task ObjectIdentifierClaim_TakesPrecedenceOverNameIdentifier_WhenResolvingOwnershipKey()
    {
        // The Entra v2.0 shape that motivated the finding: oid is a GUID, NameIdentifier (sub) is opaque.
        await fixture.ResetDatabaseAsync();
        var client = fixture.CreateClient();
        const string oid = "aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee";

        var product = await CreateProductAsync(client, subject: "opaque-pairwise-sub", oid, name: "Oid Widget");

        product.CreatedBy.ShouldBe(oid);
    }

    [Fact]
    public async Task AuthenticatedCallerWithNoSubjectClaim_IsRefusedWithForbidden_NoRowPersistedOrReadable()
    {
        // R3 — deny-closed, never a crash. A null ownership key must be a clean, controlled refusal — never
        // the raw 500/leaked-EF-detail this exact scenario produced before this round's fix — and the refusal
        // must mean the row was never persisted at all, not merely unreadable afterwards.
        await fixture.ResetDatabaseAsync();
        var client = fixture.CreateClient();

        using var createRequest = new HttpRequestMessage(HttpMethod.Post, "/v1/products")
        {
            Content = JsonContent.Create(new { name = "Orphan Widget", price = 1.23m })
        };
        // Deliberately no X-Test-Subject / X-Test-Oid header — authenticated, but no resolvable subject claim.
        using var createResponse = await client.SendAsync(createRequest);

        createResponse.StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        using var listRequest = new HttpRequestMessage(HttpMethod.Get, "/v1/products?pageSize=100");
        using var listResponse = await client.SendAsync(listRequest);
        var envelope = await listResponse.Content.ReadFromJsonAsync<ProductListEnvelope>();
        envelope!.Items.ShouldNotContain(p => p.Name == "Orphan Widget");

        using var scope = fixture.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<CoreDbContext>();
        var persisted = await dbContext.Set<Product>().IgnoreQueryFilters()
            .AnyAsync(p => p.Name == "Orphan Widget");
        persisted.ShouldBeFalse("a refused write must not leave a row behind, System-owned or otherwise");
    }

    private static async Task<ProductDto> CreateProductAsync(HttpClient client, string subject, string? oid, string name)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/v1/products")
        {
            Content = JsonContent.Create(new { name, price = 9.99m })
        };
        request.Headers.Add(MultiSubjectAuthHandler.SubjectHeaderName, subject);
        if (oid != null)
        {
            request.Headers.Add(MultiSubjectAuthHandler.ObjectIdHeaderName, oid);
        }

        using var response = await client.SendAsync(request);
        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        return (await response.Content.ReadFromJsonAsync<ProductDto>())!;
    }

    private sealed record ProductListEnvelope(List<ProductDto> Items);
}
