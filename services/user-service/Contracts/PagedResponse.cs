namespace BuildNexus.UserService.Contracts;

/// <summary>
/// One page of results, with everything the caller needs to page through the
/// rest: which page this is, how big a page is, and how many rows there are in
/// total.
/// </summary>
/// <remarks>
/// <see cref="TotalPages"/> is derived rather than stored, so it cannot drift
/// away from the count it is computed from. It is at least 1 even when nothing
/// matched — "page 1 of 0" reads as a bug to anyone looking at it.
/// </remarks>
public class PagedResponse<T>
{
    public IReadOnlyList<T> Items { get; init; } = [];

    public int Page { get; init; }

    public int PageSize { get; init; }

    /// <summary>Rows matching the filter, across every page.</summary>
    public int TotalCount { get; init; }

    public int TotalPages => TotalCount == 0 ? 1 : (TotalCount + PageSize - 1) / PageSize;
}
