using System.Text.Json.Nodes;
using DataSurface.Core.Enums;
using DataSurface.EFCore.Contracts;
using DataSurface.EFCore.Interfaces;

namespace DataSurface.Dynamic.Services;

/// <summary>
/// Routes CRUD operations to the appropriate implementation based on the resolved resource backend.
/// </summary>
public sealed class DataSurfaceCrudRouter : IDataSurfaceCrudService
{
    private readonly IResourceContractProvider _contracts;
    private readonly IDataSurfaceCrudService _ef;
    private readonly IDataSurfaceCrudService _dyn;

    /// <summary>
    /// Creates a new router.
    /// </summary>
    /// <param name="contracts">The contract provider used to resolve the resource backend.</param>
    /// <param name="ef">The EF Core CRUD service implementation.</param>
    /// <param name="dyn">The dynamic CRUD service implementation.</param>
    public DataSurfaceCrudRouter(
        IResourceContractProvider contracts,
        IDataSurfaceCrudService ef,
        DynamicDataSurfaceCrudService dyn)
    {
        _contracts = contracts;
        _ef = ef;
        _dyn = dyn;
    }

    private IDataSurfaceCrudService Pick(string resourceKey)
    {
        var c = _contracts.GetByResourceKey(resourceKey);
        return c.Backend == StorageBackend.EfCore ? _ef : _dyn;
    }

    /// <inheritdoc />
    public Task<PagedResult<JsonObject>> ListAsync(string resourceKey, QuerySpec spec, ExpandSpec? expand = null, CancellationToken ct = default)
        => Pick(resourceKey).ListAsync(resourceKey, spec, expand, ct);

    /// <inheritdoc />
    public Task<JsonObject?> GetAsync(string resourceKey, object id, ExpandSpec? expand = null, CancellationToken ct = default)
        => Pick(resourceKey).GetAsync(resourceKey, id, expand, ct);

    /// <inheritdoc />
    public Task<JsonObject> CreateAsync(string resourceKey, JsonObject body, CancellationToken ct = default)
        => Pick(resourceKey).CreateAsync(resourceKey, body, ct);

    /// <inheritdoc />
    public Task<JsonObject> UpdateAsync(string resourceKey, object id, JsonObject patch, CancellationToken ct = default)
        => Pick(resourceKey).UpdateAsync(resourceKey, id, patch, ct);

    /// <inheritdoc />
    public Task DeleteAsync(string resourceKey, object id, CrudDeleteSpec? deleteSpec = null, CancellationToken ct = default)
        => Pick(resourceKey).DeleteAsync(resourceKey, id, deleteSpec, ct);
}
