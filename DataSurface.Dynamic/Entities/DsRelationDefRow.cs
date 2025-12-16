using DataSurface.Core.Enums;

namespace DataSurface.Dynamic.Entities;

/// <summary>
/// Database row representing a dynamic relation definition.
/// </summary>
public sealed class DsRelationDefRow
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
    /// Gets or sets the internal name for the relation.
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
    /// Gets or sets the target entity key.
    /// </summary>
    public string TargetEntityKey { get; set; } = default!;

    /// <summary>
    /// Gets or sets whether the relation may be expanded.
    /// </summary>
    public bool ExpandAllowed { get; set; }
    /// <summary>
    /// Gets or sets whether the relation is expanded by default.
    /// </summary>
    public bool DefaultExpanded { get; set; }

    /// <summary>
    /// Gets or sets how the relation is written.
    /// </summary>
    public RelationWriteMode WriteMode { get; set; } = RelationWriteMode.NestedDisabled;
    /// <summary>
    /// Gets or sets the API field name used for relation writes.
    /// </summary>
    public string? WriteFieldName { get; set; }
    /// <summary>
    /// Gets or sets whether the relation write is required on create.
    /// </summary>
    public bool RequiredOnCreate { get; set; }

    /// <summary>
    /// Gets or sets the optional foreign key property name.
    /// </summary>
    public string? ForeignKeyProperty { get; set; } // semantic only in dynamic case

    /// <summary>
    /// Gets or sets the UTC timestamp when this definition was last updated.
    /// </summary>
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
