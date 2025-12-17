using System.Text.Json.Nodes;
using DataSurface.Core.Contracts;
using DataSurface.EFCore.Contracts;

namespace DataSurface.EFCore.Interfaces;

/// <summary>
/// Provides async streaming support for large data exports.
/// </summary>
/// <remarks>
/// <para>
/// Streaming enables efficient export of large datasets without loading everything into memory.
/// Results are yielded as they are retrieved from the database.
/// </para>
/// <code>
/// await foreach (var item in streamingService.StreamAsync("User", spec))
/// {
///     await writer.WriteLineAsync(item.ToJsonString());
/// }
/// </code>
/// </remarks>
public interface IDataSurfaceStreamingService
{
    /// <summary>
    /// Streams all matching resources as an async enumerable.
    /// </summary>
    /// <param name="resourceKey">The resource key.</param>
    /// <param name="spec">The query specification (pagination is ignored for streaming).</param>
    /// <param name="expand">Optional expand specification.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>An async enumerable of JSON objects.</returns>
    IAsyncEnumerable<JsonObject> StreamAsync(
        string resourceKey,
        QuerySpec spec,
        ExpandSpec? expand = null,
        CancellationToken ct = default);

    /// <summary>
    /// Streams all matching resources with batch processing.
    /// </summary>
    /// <param name="resourceKey">The resource key.</param>
    /// <param name="spec">The query specification.</param>
    /// <param name="batchSize">The batch size for database queries.</param>
    /// <param name="expand">Optional expand specification.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>An async enumerable of JSON object batches.</returns>
    IAsyncEnumerable<IReadOnlyList<JsonObject>> StreamBatchesAsync(
        string resourceKey,
        QuerySpec spec,
        int batchSize = 1000,
        ExpandSpec? expand = null,
        CancellationToken ct = default);
}
