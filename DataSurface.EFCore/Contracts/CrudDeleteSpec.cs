namespace DataSurface.EFCore.Contracts;

/// <summary>
/// Specifies how delete operations should be performed.
/// </summary>
/// <param name="HardDelete">
/// When <see langword="true"/>, performs a hard delete; otherwise, a soft delete may be used when supported.
/// </param>
/// <param name="ConcurrencyToken">
/// Optional concurrency token (e.g., from If-Match header) to verify the entity hasn't changed before deletion.
/// </param>
public sealed record CrudDeleteSpec(bool HardDelete = false, string? ConcurrencyToken = null);