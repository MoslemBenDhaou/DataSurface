using DataSurface.Core.Enums;

namespace DataSurface.Dynamic.Entities;

/// <summary>
/// Database row representing a dynamic property definition.
/// </summary>
public sealed class DsPropertyDefRow
{
    /// <summary>
    /// Gets or sets the database identifier.
    /// </summary>
    public int Id { get; set; }

    /// <summary>
    /// Gets or sets the owning entity definition identifier.
    /// </summary>
    public int EntityDefId { get; set; }
    /// <summary>
    /// Gets or sets the owning entity definition navigation.
    /// </summary>
    public DsEntityDefRow EntityDef { get; set; } = default!;

    /// <summary>
    /// Gets or sets the internal name for the property.
    /// </summary>
    public string Name { get; set; } = default!;
    /// <summary>
    /// Gets or sets the external (API) name for the property.
    /// </summary>
    public string ApiName { get; set; } = default!;

    /// <summary>
    /// Gets or sets the field type.
    /// </summary>
    public FieldType Type { get; set; }
    /// <summary>
    /// Gets or sets whether the field is nullable.
    /// </summary>
    public bool Nullable { get; set; }

    /// <summary>
    /// Gets or sets the DTO membership flags for the property.
    /// </summary>
    public CrudDto InFlags { get; set; } = CrudDto.Read;

    /// <summary>
    /// Gets or sets whether the field is required on create.
    /// </summary>
    public bool RequiredOnCreate { get; set; }
    /// <summary>
    /// Gets or sets whether the field is immutable.
    /// </summary>
    public bool Immutable { get; set; }
    /// <summary>
    /// Gets or sets whether the field is hidden.
    /// </summary>
    public bool Hidden { get; set; }

    /// <summary>
    /// Gets or sets whether the field is indexed.
    /// </summary>
    public bool Indexed { get; set; }

    // validation (optional)
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
    /// Gets or sets an optional regular expression constraint.
    /// </summary>
    public string? Regex { get; set; }

    // concurrency token marker (optional for dynamic)
    /// <summary>
    /// Gets or sets whether the field participates as a concurrency token.
    /// </summary>
    public bool ConcurrencyToken { get; set; }
    /// <summary>
    /// Gets or sets the concurrency mode.
    /// </summary>
    public ConcurrencyMode ConcurrencyMode { get; set; } = ConcurrencyMode.RowVersion;
    /// <summary>
    /// Gets or sets whether the concurrency token is required on update.
    /// </summary>
    public bool ConcurrencyRequiredOnUpdate { get; set; } = true;

    /// <summary>
    /// Gets or sets the UTC timestamp when this definition was last updated.
    /// </summary>
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
