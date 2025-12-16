using DataSurface.Core.Enums;

namespace DataSurface.Core.Contracts;

/// <summary>
/// Runtime (dynamic) relation definition used to build a <see cref="RelationContract"/>.
/// </summary>
public sealed record RelationDef(
    string Name,
    string ApiName,
    RelationKind Kind,
    string TargetResourceKey,
    bool ExpandAllowed,
    bool DefaultExpanded,
    RelationWriteMode WriteMode,
    string? WriteFieldName,
    bool RequiredOnCreate,
    string? ForeignKeyProperty
);