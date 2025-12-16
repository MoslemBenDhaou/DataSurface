namespace DataSurface.EFCore.Interfaces;

/// <summary>
/// Interface for entities that track creation and modification timestamps.
/// </summary>
/// <remarks>
/// When enabled, the <see cref="Context.DeclarativeDbContext{TContext}"/> automatically populates
/// <see cref="CreatedAt"/> on insert and <see cref="UpdatedAt"/> on insert/update.
/// </remarks>
public interface ITimestamped
{
    /// <summary>
    /// Gets or sets the UTC timestamp when the entity was created.
    /// </summary>
    DateTime CreatedAt { get; set; }

    /// <summary>
    /// Gets or sets the UTC timestamp when the entity was last updated.
    /// </summary>
    DateTime UpdatedAt { get; set; }
}
