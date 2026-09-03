namespace BuildNexus.UserService.Models;

/// <summary>
/// One page of a larger result set, with the size of that whole set beside it.
/// </summary>
/// <remarks>
/// <see cref="TotalCount"/> is what makes a page usable: without it a caller
/// cannot tell a last page from a full one, and cannot render "page 2 of 7".
/// It counts the rows matching the same filter as <see cref="Items"/>, not the
/// table.
/// </remarks>
public class PagedResult<T>
{
    public IReadOnlyList<T> Items { get; init; } = [];

    /// <summary>Rows matching the filter, across every page.</summary>
    public int TotalCount { get; init; }
}
