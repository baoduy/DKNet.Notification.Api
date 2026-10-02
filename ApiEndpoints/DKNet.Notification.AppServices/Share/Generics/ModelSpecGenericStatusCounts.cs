using System.Linq.Dynamic.Core;
using DKNet.EfCore.Specifications.Definitions;
using DKNet.EfCore.Specifications.Repositories;
using DKNet.Notification.Domains.Share;

namespace DKNet.Notification.AppServices.Share.Generics;

/// <summary>
/// Parameters used to filter status-count queries by date range.
/// </summary>
public sealed record GenericStatusCountsParameters
{
    /// <summary>
    ///     Start date for filtering (no lower bound if not provided)
    /// </summary>
    public DateTimeOffset? From { get; init; }

    /// <summary>
    ///     End date for filtering (no upper bound if not provided)
    /// </summary>
    public DateTimeOffset? To { get; init; }
}

/// <summary>
/// Describes a status property to group by when computing counts.
/// </summary>
/// <param name="Name">The entity property name that contains the status value.</param>
/// <param name="EnumType">The enum type that defines the full set of expected status values.</param>
public record StatusPropertyInfo(string Name, Type EnumType);

/// <summary>
/// Represents a status bucket and its aggregated count.
/// </summary>
/// <param name="Type">The status enum type name.</param>
/// <param name="Status">The status value name.</param>
/// <param name="Count">The number of matching records.</param>
public sealed record StatusCountsResult(string Type, string Status, int Count);

/// <summary>
/// Specification that restricts entities to a created-on date window for status aggregation.
/// </summary>
/// <typeparam name="TEntity">The entity type queried for status counts.</typeparam>
public class ModelSpecStatusCounts<TEntity> : Specification<TEntity>
    where TEntity : DomainEntity
{
    #region Constructors

    /// <summary>
    /// Initializes a new specification for status counting in the requested date range.
    /// </summary>
    /// <param name="parameters">The date range parameters used to build the filter.</param>
    public ModelSpecStatusCounts(GenericStatusCountsParameters parameters)
    {
        var predicate = CreatePredicate(x => true);

        if (parameters.From is { } from)
        {
            predicate = predicate.And(x => x.CreatedOn >= from);
        }

        if (parameters.To is { } to)
        {
            predicate = predicate.And(x => x.CreatedOn <= to);
        }

        WithFilter(predicate);
    }

    #endregion
}

/// <summary>
/// Extension methods for querying grouped status counts from repositories.
/// </summary>
public static class ModelSpecGenericStatusCountsExtensions
{
    #region Methods

    /// <summary>
    /// Returns status counts for the requested property, including zero-count values for missing enum members.
    /// </summary>
    /// <param name="repo">The repository used to execute the query.</param>
    /// <param name="property">Metadata describing the status property and enum type.</param>
    /// <param name="parameters">Date-range parameters applied to the base query.</param>
    /// <typeparam name="TEntity">The entity type queried from the repository.</typeparam>
    /// <returns>A collection of status counts for all enum values.</returns>
    public static async Task<ICollection<StatusCountsResult>> GetStatusCounts<TEntity>(
        this IRepositorySpec repo,
        StatusPropertyInfo property, GenericStatusCountsParameters parameters)
        where TEntity : DomainEntity
    {
        var query = repo.Query(new ModelSpecStatusCounts<TEntity>(parameters));

        // Group by the given property name and project to dynamic with Status and Count
        var groupedDynamic = await query
            .GroupBy(property.Name)
            .Select("new (Key as Status, Count() as Count)")
            .ToDynamicListAsync()
            .ConfigureAwait(false);

        // Build lookup from existing results (case-insensitive)
        var existing = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var d in groupedDynamic)
        {
            var key = d.Status?.ToString().ToUpperInvariant() ?? string.Empty;
            var count = (int)d.Count;
            if (!string.IsNullOrWhiteSpace(key)) existing[key] = count;
        }

        // Ensure all enum values are present with zero counts when missing
        var allNames = Enum.GetNames(property.EnumType).Select(n => n.ToUpperInvariant()).ToArray();
        var result = new List<StatusCountsResult>(allNames.Length);
        foreach (var name in allNames)
        {
            existing.TryGetValue(name, out var count);
            result.Add(new StatusCountsResult(property.EnumType.Name, name, count));
        }

        return result;
    }

    #endregion
}