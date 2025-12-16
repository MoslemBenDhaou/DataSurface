using DataSurface.Core.Enums;

namespace DataSurface.Core.Contracts;

/// <summary>
/// Security configuration for a resource, expressed as per-operation policy names.
/// </summary>
/// <param name="Policies">Policy name per CRUD operation.</param>
public sealed record SecurityContract(
    IReadOnlyDictionary<CrudOperation, string?> Policies
);