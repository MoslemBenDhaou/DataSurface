using System.Text.Json.Nodes;
using DataSurface.Dynamic.Contracts;
using DataSurface.Dynamic.Entities;
using DataSurface.Dynamic.Indexing;
using Microsoft.EntityFrameworkCore;

namespace DataSurface.Admin.Services;

/// <summary>
/// Service that rebuilds dynamic indexes for a given entity based on stored JSON records.
/// </summary>
public sealed class DynamicIndexRebuildService
{
    private readonly DbContext _db;
    private readonly DynamicResourceContractProvider _contracts;
    private readonly IDynamicIndexService _index;

    /// <summary>
    /// Creates a new instance of the index rebuild service.
    /// </summary>
    /// <param name="db">The EF Core database context.</param>
    /// <param name="contracts">The dynamic contract provider.</param>
    /// <param name="index">The indexing service.</param>
    public DynamicIndexRebuildService(DbContext db, DynamicResourceContractProvider contracts, IDynamicIndexService index)
    {
        _db = db;
        _contracts = contracts;
        _index = index;
    }

    /// <summary>
    /// Rebuilds indexes for all non-deleted records of the given entity.
    /// </summary>
    /// <param name="entityKey">The entity key to rebuild indexes for.</param>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>The number of records processed.</returns>
    public async Task<int> RebuildEntityAsync(string entityKey, CancellationToken ct)
    {
        var c = await _contracts.GetByResourceKeyAsync(entityKey, ct);

        var rows = await _db.Set<DsDynamicRecordRow>()
            .Where(r => r.EntityKey == entityKey && !r.IsDeleted)
            .ToListAsync(ct);

        var count = 0;
        foreach (var row in rows)
        {
            var obj = JsonNode.Parse(row.DataJson)?.AsObject();
            if (obj is null) continue;

            await _index.RebuildIndexesAsync(entityKey, row.Id, c, obj, ct);
            count++;
        }

        return count;
    }
}
