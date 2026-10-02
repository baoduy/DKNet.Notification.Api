using DKNet.EfCore.Specifications.Repositories;
using DKNet.Notification.AppServices.Share.Generics;
using DKNet.Notification.Domains.Share;

// ReSharper disable once CheckNamespace
namespace Microsoft.AspNetCore.Builder;

/// <summary>
///     Template-local endpoint mapper for status-count aggregation. Not part of the published
///     <c>DKNet.AspCore.Extensions</c> package — <see cref="DKNet.Notification.AppServices.Share.Generics" /> stays template-local
///     (see DRK-500 §4).
/// </summary>
[ExcludeFromCodeCoverage]
internal static class StatusCountsEndpointMapperExtensions
{
    extension(RouteGroupBuilder app)
    {
        /// <summary>
        ///     Gets status counts endpoint.
        /// </summary>
        /// <param name="properties"></param>
        /// <param name="endpoint"></param>
        /// <typeparam name="TEntity"></typeparam>
        /// <returns></returns>
        public RouteHandlerBuilder MapGetStatusCounts<TEntity>(string endpoint = "status",
            params StatusPropertyInfo[] properties) where TEntity : DomainEntity
        {
            return app.MapGet(endpoint,
                    async ([AsParameters] GenericStatusCountsParameters parameters,
                        [FromServices] IRepositorySpec repo) =>
                    {
                        var results = new List<StatusCountsResult>();
                        foreach (var property in properties)
                        {
                            var counts = await repo.GetStatusCounts<TEntity>(property, parameters).ConfigureAwait(false);
                            results.AddRange(counts);
                        }
                        return Results.Ok(results);
                    })
                .CacheOutput()
                .ProducesCommons()
                .Produces<List<StatusCountsResult>>()
                .WithDescription(
                    $"Retrieve grouped counts of '{string.Join(',', properties.Select(p => p.Name))}' for {typeof(TEntity).Name} within date range.");
        }
    }
}
