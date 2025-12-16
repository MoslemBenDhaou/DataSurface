namespace DataSurface.Core.Annotations;

/// <summary>
/// Marks a property as the primary key for a resource.
/// </summary>
[AttributeUsage(AttributeTargets.Property, AllowMultiple = false, Inherited = true)]
public sealed class CrudKeyAttribute : Attribute
{
    /// <summary>
    /// Gets or sets the external (API) name for the key field.
    /// </summary>
    public string? ApiName { get; init; }
}
