namespace DataSurface.EFCore.Contracts;

/// <summary>
/// Specifies how delete operations should be performed.
/// </summary>
/// <param name="HardDelete">
/// When <see langword="true"/>, performs a hard delete; otherwise, a soft delete may be used when supported.
/// </param>
public sealed record CrudDeleteSpec(bool HardDelete = false);