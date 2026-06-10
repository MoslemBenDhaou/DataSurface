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
    // Records are processed in pages so a large entity does not get materialized (tracked)
    // in memory at once, and index changes are saved once per page rather than per record.
    private const int BatchSize = 200;

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

        var count = 0;
        var page = 0;

        while (true)
        {
            var rows = await _db.Set<DsDynamicRecordRow>()
                .AsNoTracking()
                .Where(r => r.EntityKey == entityKey && !r.IsDeleted)
                .OrderBy(r => r.Id)
                .Skip(page * BatchSize)
                .Take(BatchSize)
                .ToListAsync(ct);

            if (rows.Count == 0) break;

            foreach (var row in rows)
            {
                var obj = JsonNode.Parse(row.DataJson)?.AsObject();
                if (obj is null) continue;

                // Stages old-row removal (async) + fresh rows on the change tracker.
                await _index.StageIndexRowsAsync(entityKey, row.Id, c, obj, ct);
                count++;
            }

            // One SaveChanges per page instead of per record.
            await _db.SaveChangesAsync(ct);
            _db.ChangeTracker.Clear();

            page++;
        }

        return count;
    }
}
