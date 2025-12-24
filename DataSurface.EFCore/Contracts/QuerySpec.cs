namespace DataSurface.EFCore.Contracts;

/// <summary>
/// Query parameters for list operations (paging, sorting, filtering, searching, and field projection).
/// </summary>
/// <param name="Page">1-based page number.</param>
/// <param name="PageSize">Requested page size.</param>
/// <param name="Sort">Sort expression (for example <c>"title,-id"</c>).</param>
/// <param name="Filters">Map of field API name to a filter expression.</param>
/// <param name="Search">Full-text search query (q parameter) applied to searchable fields.</param>
/// <param name="Fields">Optional field projection - only return specified fields (comma-separated API names).</param>
/// <remarks>
/// When used with <see cref="DataSurface.EFCore.EfCrudQueryEngine"/>, filter values may be specified as
/// <c>"op:value"</c> (for example <c>"gte:10"</c>) or just <c>"value"</c> (defaults to equality).
/// Use <c>"isnull:true"</c> or <c>"isnull:false"</c> to filter null/non-null values.
/// </remarks>
public sealed record QuerySpec(
    int Page = 1,
    int PageSize = 20,
    string? Sort = null,
    IReadOnlyDictionary<string, string>? Filters = null,
    string? Search = null,
    string? Fields = null
);
