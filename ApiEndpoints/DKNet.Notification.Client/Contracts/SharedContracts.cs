namespace DKNet.Notification.Client.Contracts;

/// <summary>One page of a list call.</summary>
public sealed record PagedResponse<T>
{
    public IReadOnlyList<T> Items { get; init; } = [];

    public int PageCount { get; init; }

    public int PageNumber { get; init; }

    public int PageSize { get; init; }

    public int TotalItemCount { get; init; }

    public bool HasNextPage { get; init; }

    public bool HasPreviousPage { get; init; }
}

/// <summary>The RFC 9457 problem details the API returns when it refuses a call.</summary>
public sealed record ProblemDetailsResponse
{
    public string? Type { get; init; }

    public string? Title { get; init; }

    public int? Status { get; init; }

    public string? Detail { get; init; }

    public string? TraceId { get; init; }
}
