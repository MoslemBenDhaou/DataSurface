namespace DataSurface.EFCore.Contracts;

/// <summary>
/// Query parameters for list operations (paging, sorting, and filtering).
/// </summary>
/// <param name="Page">1-based page number.</param>
/// <param name="PageSize">Requested page size.</param>
/// <param name="Sort">Sort expression (for example <c>"title,-id"</c>).</param>
/// <param name="Filters">Map of field API name to a filter expression.</param>
/// <remarks>
/// When used with <see cref="DataSurface.EFCore.EfCrudQueryEngine"/>, filter values may be specified as
/// <c>"op:value"</c> (for example <c>"gte:10"</c>) or just <c>"value"</c> (defaults to equality).
/// </remarks>
public sealed record QuerySpec(
    int Page = 1,
    int PageSize = 20,
    string? Sort = null,
    IReadOnlyDictionary<string, string>? Filters = null
);
