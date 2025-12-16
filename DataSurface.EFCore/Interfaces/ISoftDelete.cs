namespace DataSurface.EFCore.Interfaces;

/// <summary>
/// Marker interface for entities that support soft deletion.
/// </summary>
/// <remarks>
/// When enabled, the EF Core integration can apply a global query filter that excludes entities where
/// <see cref="IsDeleted"/> is <see langword="true"/>.
/// </remarks>
public interface ISoftDelete
{
    /// <summary>
    /// Gets or sets whether the entity is soft-deleted.
    /// </summary>
    bool IsDeleted { get; set; }
}
