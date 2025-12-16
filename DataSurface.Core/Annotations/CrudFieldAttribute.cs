using DataSurface.Core.Enums;

namespace DataSurface.Core.Annotations;

/// <summary>
/// Declares a scalar property as part of the resource contract and controls its DTO membership.
/// </summary>
[AttributeUsage(AttributeTargets.Property, AllowMultiple = false)]
public sealed class CrudFieldAttribute : Attribute
{
    /// <summary>
    /// Creates a new field marker.
    /// </summary>
    /// <param name="in">Flags that indicate which DTO shapes the field participates in.</param>
    public CrudFieldAttribute(CrudDto @in) => In = @in;

    /// <summary>
    /// Gets the DTO membership flags for the field.
    /// </summary>
    public CrudDto In { get; }

    /// <summary>
    /// Gets or sets the external (API) name for this field. If not set, a camelCase name is derived from the CLR name.
    /// </summary>
    public string? ApiName { get; set; }             // rename externally
    /// <summary>
    /// Gets or sets whether this field is required when creating a resource.
    /// </summary>
    public bool RequiredOnCreate { get; set; }
    /// <summary>
    /// Gets or sets whether this field is immutable (cannot be set during updates).
    /// </summary>
    public bool Immutable { get; set; }              // cannot update
    /// <summary>
    /// Gets or sets whether this field is hidden and never accepted/emitted (hard deny).
    /// </summary>
    public bool Hidden { get; set; }                 // hard deny (never exposed)

    // common validation hints
    /// <summary>
    /// Gets or sets a minimum string length constraint.
    /// </summary>
    public int? MinLength { get; set; }
    /// <summary>
    /// Gets or sets a maximum string length constraint.
    /// </summary>
    public int? MaxLength { get; set; }
    /// <summary>
    /// Gets or sets a minimum numeric value constraint.
    /// </summary>
    public decimal? Min { get; set; }
    /// <summary>
    /// Gets or sets a maximum numeric value constraint.
    /// </summary>
    public decimal? Max { get; set; }
    /// <summary>
    /// Gets or sets a regular expression constraint applied to string values.
    /// </summary>
    public string? Regex { get; set; }
}