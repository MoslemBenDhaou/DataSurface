namespace DataSurface.EFCore.Contracts;

/// <summary>
/// Specifies which relations should be expanded during read operations.
/// </summary>
/// <param name="Expand">The list of relation API names to expand.</param>
public sealed record ExpandSpec(IReadOnlyList<string> Expand);