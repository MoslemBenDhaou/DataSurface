namespace DataSurface.Core.Contracts;

/// <summary>
/// Query allowlists and paging limits for a resource.
/// </summary>
/// <param name="MaxPageSize">Maximum page size allowed for list queries.</param>
/// <param name="FilterableFields">Allowlist of fields that may be used in filtering expressions (API names).</param>
/// <param name="SortableFields">Allowlist of fields that may be used in sort expressions (API names).</param>
/// <param name="SearchableFields">Allowlist of fields included in full-text search (q parameter).</param>
/// <param name="DefaultSort">Optional default sort expression.</param>
public sealed record QueryContract(
    int MaxPageSize,
    IReadOnlyList<string> FilterableFields,
    IReadOnlyList<string> SortableFields,
    IReadOnlyList<string> SearchableFields,
    string? DefaultSort
);