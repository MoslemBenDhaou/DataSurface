namespace DataSurface.Core.Contracts;

/// <summary>
/// Read-time rules for relation expansion and projection.
/// </summary>
/// <param name="ExpandAllowed">Allowlist of relations that may be expanded (API names).</param>
/// <param name="MaxExpandDepth">Maximum allowed expansion depth.</param>
/// <param name="DefaultExpand">Relations expanded by default (API names).</param>
public sealed record ReadContract(
    IReadOnlyList<string> ExpandAllowed,
    int MaxExpandDepth,
    IReadOnlyList<string> DefaultExpand
);