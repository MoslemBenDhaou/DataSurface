using DataSurface.Core.Enums;

namespace DataSurface.Core.Annotations;

/// <summary>
/// Marks a property as the resource concurrency token used during updates.
/// </summary>
[AttributeUsage(AttributeTargets.Property, AllowMultiple = false)]
public sealed class CrudConcurrencyAttribute : Attribute
{
    /// <summary>
    /// Gets or sets the concurrency mechanism.
    /// </summary>
    public ConcurrencyMode Mode { get; set; } = ConcurrencyMode.RowVersion;
    /// <summary>
    /// Gets or sets whether the concurrency token must be provided on update.
    /// </summary>
    public bool RequiredOnUpdate { get; set; } = true;

    /// <summary>
    /// Creates a new concurrency marker.
    /// </summary>
    public CrudConcurrencyAttribute() { }
}