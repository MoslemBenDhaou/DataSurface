using System.Text.Json.Nodes;
using DataSurface.Core.Contracts;
using DataSurface.Core.Enums;
using DataSurface.Dynamic.Entities;
using Microsoft.EntityFrameworkCore;

namespace DataSurface.Dynamic.Indexing;

/// <summary>
/// EF Core implementation of <see cref="IDynamicIndexService"/>.
/// </summary>
public sealed class EfDynamicIndexService : IDynamicIndexService
{
    private readonly DbContext _db;

    /// <summary>
    /// Creates a new index service.
    /// </summary>
    /// <param name="db">The EF Core database context.</param>
    public EfDynamicIndexService(DbContext db) => _db = db;

    /// <inheritdoc />
    /// <summary>
    /// Rebuilds the indexes for the specified entity and record.
    /// </summary>
    /// <param name="entityKey">The key of the entity.</param>
    /// <param name="recordId">The ID of the record.</param>
    /// <param name="contract">The resource contract.</param>
    /// <param name="json">The JSON data.</param>
    /// <param name="ct">The cancellation token.</param>
    public async Task RebuildIndexesAsync(string entityKey, string recordId, ResourceContract contract, JsonObject json, CancellationToken ct)
    {
        // delete old rows
        var old = _db.Set<DsDynamicIndexRow>()
            .Where(x => x.EntityKey == entityKey && x.RecordId == recordId);

        _db.RemoveRange(old);

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

            FillTypedValue(row, f.Type, node);
            _db.Add(row);
        }

        await _db.SaveChangesAsync(ct);
    }

    private static void FillTypedValue(DsDynamicIndexRow row, FieldType t, JsonNode node)
    {
        try
        {
            switch (t)
            {
                case FieldType.Guid:
                    row.ValueGuid = Guid.Parse(node.ToJsonString().Trim('"'));
                    break;

                case FieldType.Boolean:
                    row.ValueBool = node.GetValue<bool>();
                    break;

                case FieldType.Int32:
                case FieldType.Int64:
                case FieldType.Decimal:
                    row.ValueNumber = decimal.Parse(node.ToJsonString().Trim('"'), System.Globalization.CultureInfo.InvariantCulture);
                    break;

                case FieldType.DateTime:
                    row.ValueDateTime = DateTime.Parse(node.ToJsonString().Trim('"'), System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.RoundtripKind);
                    break;

                default:
                    // String/Enum/Json fallback
                    row.ValueString = node.ToJsonString().Trim('"');
                    break;
            }
        }
        catch
        {
            // If parsing fails, store as string fallback for debugging.
            row.ValueString = node.ToJsonString().Trim('"');
        }
    }
}
