using DataSurface.Core.Enums;

namespace DataSurface.Core.Annotations;

/// <summary>
/// Declares a navigation property as a relation in the resource contract.
/// </summary>
[AttributeUsage(AttributeTargets.Property, AllowMultiple = false)]
public sealed class CrudRelationAttribute : Attribute
{
    /// <summary>
    /// Gets or sets the relationship kind. When <see langword="null"/>, the kind may be inferred.
    /// </summary>
    public RelationKind? Kind { get; set; } // optional; inferred if null

    // Read
    /// <summary>
    /// Gets or sets whether this relation may be expanded during reads.
    /// </summary>
    public bool ReadExpandAllowed { get; set; } = false;
    /// <summary>
    /// Gets or sets whether this relation is expanded by default.
    /// </summary>
    public bool DefaultExpanded { get; set; } = false;

    // Write
    /// <summary>
    /// Gets or sets how writes to this relation are performed.
    /// </summary>
    public RelationWriteMode WriteMode { get; set; } = RelationWriteMode.NestedDisabled;
    /// <summary>
    /// Gets or sets the field name used for relation writes (for example <c>"userId"</c> or <c>"tagIds"</c>).
    /// </summary>
    public string? WriteFieldName { get; set; } // e.g. userId or tagIds
    /// <summary>
    /// Gets or sets whether a relation write is required on create.
    /// </summary>
    public bool RequiredOnCreate { get; set; } = false;

    // FK (for many-to-one)
    /// <summary>
    /// Gets or sets the CLR foreign-key property name (for example <c>"UserId"</c>) used for many-to-one relations.
    /// </summary>
    public string? ForeignKeyProperty { get; set; } // e.g. UserId
}