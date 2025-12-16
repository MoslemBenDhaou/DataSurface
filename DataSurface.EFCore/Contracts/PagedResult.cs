namespace DataSurface.EFCore.Contracts;

/// <summary>
/// Represents a page of results for a list operation.
/// </summary>
/// <typeparam name="T">The item type.</typeparam>
/// <param name="Items">The items in the page.</param>
/// <param name="Page">The 1-based page number.</param>
/// <param name="PageSize">The page size.</param>
/// <param name="Total">The total number of items matching the query.</param>
public sealed record PagedResult<T>(IReadOnlyList<T> Items, int Page, int PageSize, int Total);