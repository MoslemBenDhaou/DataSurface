using DataSurface.Core.Enums;

namespace DataSurface.Admin.Dtos;

/// <summary>
/// DTO representing a dynamic property definition within an entity.
/// </summary>
public sealed class AdminPropertyDefDto
{
    /// <summary>
    /// Gets or sets the database identifier for the property definition.
    /// </summary>
    public int? Id { get; set; }

    /// <summary>
    /// Gets or sets the CLR/name identifier for the property.
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
    /// Gets or sets whether the field can be <c>null</c>.
    /// </summary>
    public bool Nullable { get; set; }

    /// <summary>
    /// Gets or sets the DTO membership flags for the property.
    /// </summary>
    public CrudDto InFlags { get; set; } = CrudDto.Read;

    /// <summary>
    /// Gets or sets whether the property is required on create.
    /// </summary>
    public bool RequiredOnCreate { get; set; }

    /// <summary>
    /// Gets or sets whether the property is immutable (cannot be updated).
    /// </summary>
    public bool Immutable { get; set; }

    /// <summary>
    /// Gets or sets whether the property is hidden (not exposed through the API).
    /// </summary>
    public bool Hidden { get; set; }

    /// <summary>
    /// Gets or sets whether the property is indexed for filtering/sorting.
    /// </summary>
    public bool Indexed { get; set; }

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
    /// Gets or sets an optional regular expression constraint for string values.
    /// </summary>
    public string? Regex { get; set; }

    /// <summary>
    /// Gets or sets whether the field participates as a concurrency token.
    /// </summary>
    public bool ConcurrencyToken { get; set; }

    /// <summary>
    /// Gets or sets the concurrency mode used for update operations.
    /// </summary>
    public ConcurrencyMode ConcurrencyMode { get; set; } = ConcurrencyMode.RowVersion;

    /// <summary>
    /// Gets or sets whether the concurrency token is required on update.
    /// </summary>
    public bool ConcurrencyRequiredOnUpdate { get; set; } = true;
}
