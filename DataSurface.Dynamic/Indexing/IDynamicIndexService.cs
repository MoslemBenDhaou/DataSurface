using System.Text.Json.Nodes;
using DataSurface.Core.Contracts;

namespace DataSurface.Dynamic.Indexing;

/// <summary>
/// Builds and maintains index rows for dynamic records to enable filtering and sorting.
/// </summary>
public interface IDynamicIndexService
{
    /// <summary>
    /// Rebuilds index rows for the specified record.
    /// </summary>
    /// <param name="entityKey">The entity key the record belongs to.</param>
    /// <param name="recordId">The record identifier.</param>
    /// <param name="contract">The resource contract used to determine indexable fields.</param>
    /// <param name="json">The stored JSON object for the record.</param>
    /// <param name="ct">A cancellation token.</param>
    Task RebuildIndexesAsync(string entityKey, string recordId, ResourceContract contract, JsonObject json, CancellationToken ct);
}
