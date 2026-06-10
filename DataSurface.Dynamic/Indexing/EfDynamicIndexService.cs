using System.Globalization;
using System.Text.Json.Nodes;
using DataSurface.Core.Contracts;
using DataSurface.Core.Enums;
using DataSurface.Dynamic.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace DataSurface.Dynamic.Indexing;

/// <summary>
/// EF Core implementation of <see cref="IDynamicIndexService"/>.
/// </summary>
public sealed class EfDynamicIndexService : IDynamicIndexService
{
    private readonly DbContext _db;
    private readonly ILogger<EfDynamicIndexService>? _logger;

    /// <summary>
    /// Creates a new index service.
    /// </summary>
    /// <param name="db">The EF Core database context.</param>
    /// <param name="logger">Optional logger used to surface index value parse failures.</param>
    public EfDynamicIndexService(DbContext db, ILogger<EfDynamicIndexService>? logger = null)
    {
        _db = db;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task StageIndexRowsAsync(string entityKey, string recordId, ResourceContract contract, JsonObject? json, CancellationToken ct)
    {
        // Stage removal of existing rows (async load; RemoveRange over IQueryable would
        // enumerate synchronously).
        var old = await _db.Set<DsDynamicIndexRow>()
            .Where(x => x.EntityKey == entityKey && x.RecordId == recordId)
            .ToListAsync(ct);

        _db.RemoveRange(old);

        if (json is null) return; // removal only (delete paths)

        // which properties to index?
        // Rule: index fields that are filterable/sortable OR (later) explicitly marked Indexed in PropertyDefRow.
        var indexable = contract.Fields
            .Where(f => !f.Hidden && (f.Filterable || f.Sortable))
            .ToList();

        foreach (var f in indexable)
        {
            if (!json.TryGetPropertyValue(f.ApiName, out var node) || node is null)
                continue;

            var row = new DsDynamicIndexRow
            {
                EntityKey = entityKey,
                RecordId = recordId,
                PropertyApiName = f.ApiName
            };

            FillTypedValue(row, f.Type, node, entityKey, f.ApiName);
            _db.Add(row);
        }
    }

    /// <inheritdoc />
    /// <summary>
    /// Rebuilds the indexes for the specified entity and record and saves immediately.
    /// </summary>
    /// <param name="entityKey">The key of the entity.</param>
    /// <param name="recordId">The ID of the record.</param>
    /// <param name="contract">The resource contract.</param>
    /// <param name="json">The JSON data.</param>
    /// <param name="ct">The cancellation token.</param>
    public async Task RebuildIndexesAsync(string entityKey, string recordId, ResourceContract contract, JsonObject json, CancellationToken ct)
    {
        await StageIndexRowsAsync(entityKey, recordId, contract, json, ct);
        await _db.SaveChangesAsync(ct);
    }

    private void FillTypedValue(DsDynamicIndexRow row, FieldType t, JsonNode node, string entityKey, string apiName)
    {
        switch (t)
        {
            case FieldType.Guid:
                if (Guid.TryParse(GetRawString(node), out var g))
                    row.ValueGuid = g;
                else
                    IndexAsStringFallback(row, node, entityKey, apiName, t);
                break;

            case FieldType.Boolean:
                if (node is JsonValue bv && bv.TryGetValue<bool>(out var b))
                    row.ValueBool = b;
                else if (bool.TryParse(GetRawString(node), out var b2))
                    row.ValueBool = b2;
                else
                    IndexAsStringFallback(row, node, entityKey, apiName, t);
                break;

            case FieldType.Int32:
            case FieldType.Int64:
            case FieldType.Decimal:
                if (node is JsonValue nv && nv.TryGetValue<decimal>(out var d))
                    row.ValueNumber = d;
                else if (decimal.TryParse(GetRawString(node), NumberStyles.Number, CultureInfo.InvariantCulture, out var d2))
                    row.ValueNumber = d2;
                else
                    IndexAsStringFallback(row, node, entityKey, apiName, t);
                break;

            case FieldType.DateTime:
                // Parse via DateTimeOffset and normalize to UTC so stored values (and the
                // comparisons against them) do not depend on the server timezone.
                if (DateTimeOffset.TryParse(GetRawString(node), CultureInfo.InvariantCulture,
                        DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var dto))
                    row.ValueDateTime = dto.UtcDateTime;
                else
                    IndexAsStringFallback(row, node, entityKey, apiName, t);
                break;

            default:
                // String/Enum/Json fallback
                row.ValueString = GetRawString(node);
                break;
        }
    }

    // Number/date parse failures are still indexed as strings for debuggability, but are no
    // longer swallowed silently: the mismatch is logged so stale/typo'd data is discoverable.
    private void IndexAsStringFallback(DsDynamicIndexRow row, JsonNode node, string entityKey, string apiName, FieldType t)
    {
        row.ValueString = GetRawString(node);
        _logger?.LogWarning(
            "Failed to parse index value for {EntityKey}.{Property} as {FieldType}; indexed as string '{Value}' instead.",
            entityKey, apiName, t, row.ValueString);
    }

    // Extracts the raw value: GetValue<string>() for string values (avoids retaining JSON
    // escaping), falling back to ToJsonString() only for non-string/non-value nodes.
    private static string GetRawString(JsonNode node)
        => node is JsonValue v && v.TryGetValue<string>(out var s)
            ? s
            : node.ToJsonString().Trim('"');
}
