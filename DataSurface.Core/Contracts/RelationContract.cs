using DataSurface.Core.Enums;

namespace DataSurface.Core.Contracts;

/// <summary>
/// Describes a relation between resources.
/// </summary>
/// <param name="Name">CLR navigation property name.</param>
/// <param name="ApiName">External API name for the relation.</param>
/// <param name="Kind">Cardinality/kind of the relationship.</param>
/// <param name="TargetResourceKey">Resource key of the related target resource.</param>
/// <param name="Read">Read behavior (expand rules).</param>
/// <param name="Write">Write behavior (ID-based writes, etc.).</param>
public sealed record RelationContract(
    string Name,
    string ApiName,
    RelationKind Kind,
    string TargetResourceKey,
    RelationReadContract Read,
    RelationWriteContract Write
);