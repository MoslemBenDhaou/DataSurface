using DataSurface.Core.Enums;

namespace DataSurface.Core.Contracts;

/// <summary>
/// Runtime (dynamic) resource definition used to build a <see cref="ResourceContract"/> without CLR attributes.
/// </summary>
/// <remarks>
/// This mirrors the unified resource contract concepts (resource key, route, backend, operations, fields, relations,
/// query/read limits, and per-operation policies).
/// </remarks>
public sealed record EntityDef(
    string EntityKey,
    string Route,
    StorageBackend Backend,
    string KeyName,
    FieldType KeyType,
    int MaxPageSize,
    int MaxExpandDepth,
    bool EnableList,
    bool EnableGet,
    bool EnableCreate,
    bool EnableUpdate,
    bool EnableDelete,
    IReadOnlyList<PropertyDef> Properties,
    IReadOnlyList<RelationDef> Relations,
    IReadOnlyDictionary<CrudOperation, string?>? Policies = null,
    TenantContract? Tenant = null
);