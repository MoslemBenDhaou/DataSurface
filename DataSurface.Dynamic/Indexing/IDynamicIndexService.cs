using System.Text.Json.Nodes;
using DataSurface.Core.Contracts;

namespace DataSurface.Dynamic.Indexing;

/// <summary>
/// Builds and maintains index rows for dynamic records to enable filtering and sorting.
/// </summary>
public interface IDynamicIndexService
{
    /// <summary>
    /// Stages index-row changes (removal of existing rows and, when <paramref name="json"/> is
    /// not null, insertion of fresh rows) on the current DbContext change tracker WITHOUT calling
    /// SaveChanges. This lets the caller persist the record and its index rows atomically with a
    /// single SaveChanges.
    /// </summary>
    /// <param name="entityKey">The entity key the record belongs to.</param>
    /// <param name="recordId">The record identifier.</param>
    /// <param name="contract">The resource contract used to determine indexable fields.</param>
    /// <param name="json">The stored JSON object for the record, or <c>null</c> to only stage removal of existing index rows.</param>
    /// <param name="ct">A cancellation token.</param>
    Task StageIndexRowsAsync(string entityKey, string recordId, ResourceContract contract, JsonObject? json, CancellationToken ct);

    /// <summary>
    /// Rebuilds index rows for the specified record and saves the changes immediately.
    /// </summary>
    /// <param name="entityKey">The entity key the record belongs to.</param>
    /// <param name="recordId">The record identifier.</param>
    /// <param name="contract">The resource contract used to determine indexable fields.</param>
    /// <param name="json">The stored JSON object for the record.</param>
    /// <param name="ct">A cancellation token.</param>
    Task RebuildIndexesAsync(string entityKey, string recordId, ResourceContract contract, JsonObject json, CancellationToken ct);
}
