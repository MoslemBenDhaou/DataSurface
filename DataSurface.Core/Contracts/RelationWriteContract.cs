using DataSurface.Core.Enums;

namespace DataSurface.Core.Contracts;

/// <summary>
/// Write behavior for a relation.
/// </summary>
/// <param name="Mode">How writes are performed (for example by ID or ID list).</param>
/// <param name="WriteFieldName">API field name used for relation writes.</param>
/// <param name="RequiredOnCreate">Whether the relation write field is required on create.</param>
/// <param name="ForeignKeyProperty">Optional CLR foreign key property name.</param>
public sealed record RelationWriteContract(
    RelationWriteMode Mode,
    string? WriteFieldName,
    bool RequiredOnCreate,
    string? ForeignKeyProperty
);