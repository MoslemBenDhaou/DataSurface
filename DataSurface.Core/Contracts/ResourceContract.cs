using DataSurface.Core.Enums;

namespace DataSurface.Core.Contracts;

/// <summary>
/// Normalized metadata contract for a resource.
/// </summary>
/// <remarks>
/// This is the single source of truth used by higher layers (validation, CRUD endpoint generation, filtering,
/// sorting, expansion, and authorization).
/// </remarks>
/// <param name="ResourceKey">Stable resource identifier.</param>
/// <param name="Route">Route segment used for endpoints.</param>
/// <param name="Backend">Backend responsible for CRUD operations.</param>
/// <param name="Key">Primary key contract.</param>
/// <param name="Query">Query allowlists and limits.</param>
/// <param name="Read">Read-time expansion rules.</param>
/// <param name="Fields">All known scalar fields.</param>
/// <param name="Relations">All known relations.</param>
/// <param name="Operations">Per-operation contract data.</param>
/// <param name="Security">Per-operation policy configuration.</param>
public sealed record ResourceContract(
    string ResourceKey,
    string Route,
    StorageBackend Backend,
    ResourceKeyContract Key,
    QueryContract Query,
    ReadContract Read,
    IReadOnlyList<FieldContract> Fields,
    IReadOnlyList<RelationContract> Relations,
    IReadOnlyDictionary<CrudOperation, OperationContract> Operations,
    SecurityContract Security
);