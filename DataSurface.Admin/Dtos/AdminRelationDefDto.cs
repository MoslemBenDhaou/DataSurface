using DataSurface.Core.Enums;

namespace DataSurface.Admin.Dtos;

/// <summary>
/// DTO representing a dynamic relation definition within an entity.
/// </summary>
public sealed class AdminRelationDefDto
{
    /// <summary>
    /// Gets or sets the database identifier for the relation definition.
    /// </summary>
    public int? Id { get; set; }

    /// <summary>
    /// Gets or sets the CLR/name identifier for the relation.
    /// </summary>
    public string Name { get; set; } = default!;

    /// <summary>
    /// Gets or sets the external (API) name for the relation.
    /// </summary>
    public string ApiName { get; set; } = default!;

    /// <summary>
    /// Gets or sets the relation kind.
    /// </summary>
    public RelationKind Kind { get; set; }

    /// <summary>
    /// Gets or sets the target entity key for the relation.
    /// </summary>
    public string TargetEntityKey { get; set; } = default!;

    /// <summary>
    /// Gets or sets whether the relation may be expanded in read operations.
    /// </summary>
    public bool ExpandAllowed { get; set; }

    /// <summary>
    /// Gets or sets whether the relation is expanded by default.
    /// </summary>
    public bool DefaultExpanded { get; set; }

    /// <summary>
    /// Gets or sets how the relation is written (nested payloads or by ID).
    /// </summary>
    public RelationWriteMode WriteMode { get; set; } = RelationWriteMode.NestedDisabled;

    /// <summary>
    /// Gets or sets the API field name used for writes when <see cref="WriteMode"/> is enabled.
    /// </summary>
    public string? WriteFieldName { get; set; }

    /// <summary>
    /// Gets or sets whether the relation is required on create.
    /// </summary>
    public bool RequiredOnCreate { get; set; }

    /// <summary>
    /// Gets or sets the optional foreign key property name used for the relation.
    /// </summary>
    public string? ForeignKeyProperty { get; set; }
}
