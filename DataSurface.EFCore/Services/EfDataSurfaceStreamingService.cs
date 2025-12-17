using System.Runtime.CompilerServices;
using System.Text.Json.Nodes;
using DataSurface.Core.Enums;
using DataSurface.EFCore.Contracts;
using DataSurface.EFCore.Interfaces;
using DataSurface.EFCore.Observability;
using Microsoft.Extensions.Logging;

namespace DataSurface.EFCore.Services;

/// <summary>
/// Entity Framework Core implementation of <see cref="IDataSurfaceStreamingService"/>.
/// </summary>
public sealed class EfDataSurfaceStreamingService : IDataSurfaceStreamingService
{
    private readonly IDataSurfaceCrudService _crud;
    private readonly IResourceContractProvider _contracts;
    private readonly ILogger<EfDataSurfaceStreamingService> _logger;
    private readonly DataSurfaceMetrics? _metrics;

    /// <summary>
    /// Creates a new streaming service instance.
    /// </summary>
    public EfDataSurfaceStreamingService(
        IDataSurfaceCrudService crud,
        IResourceContractProvider contracts,
        ILogger<EfDataSurfaceStreamingService> logger,
        DataSurfaceMetrics? metrics = null)
    {
        _crud = crud;
        _contracts = contracts;
        _logger = logger;
        _metrics = metrics;
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<JsonObject> StreamAsync(
        string resourceKey,
        QuerySpec spec,
        ExpandSpec? expand = null,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        using var activity = DataSurfaceTracing.StartOperation(resourceKey, CrudOperation.List);
        activity?.SetTag("datasurface.streaming", true);

        _logger.LogDebug("Starting stream for {Resource}", resourceKey);

        var contract = _contracts.GetByResourceKey(resourceKey);
        var batchSize = contract.Query.MaxPageSize;
        var page = 1;
        var totalStreamed = 0;

        while (!ct.IsCancellationRequested)
        {
            var batchSpec = spec with { Page = page, PageSize = batchSize };
            var result = await _crud.ListAsync(resourceKey, batchSpec, expand, ct);

            foreach (var item in result.Items)
            {
                yield return item;
                totalStreamed++;
            }

            if (result.Items.Count < batchSize || totalStreamed >= result.Total)
                break;

            page++;
        }

        sw.Stop();
        DataSurfaceTracing.RecordSuccess(activity, totalStreamed);
        _metrics?.RecordOperation(resourceKey, CrudOperation.List, sw.Elapsed.TotalMilliseconds, totalStreamed);

        _logger.LogDebug("Stream for {Resource} completed: {Count} items in {ElapsedMs}ms",
            resourceKey, totalStreamed, sw.ElapsedMilliseconds);
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<IReadOnlyList<JsonObject>> StreamBatchesAsync(
        string resourceKey,
        QuerySpec spec,
        int batchSize = 1000,
        ExpandSpec? expand = null,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        using var activity = DataSurfaceTracing.StartOperation(resourceKey, CrudOperation.List);
        activity?.SetTag("datasurface.streaming", true);
        activity?.SetTag("datasurface.batch_size", batchSize);

        _logger.LogDebug("Starting batch stream for {Resource} with batch size {BatchSize}", resourceKey, batchSize);

        var contract = _contracts.GetByResourceKey(resourceKey);
        var effectiveBatchSize = Math.Min(batchSize, contract.Query.MaxPageSize);
        var page = 1;
        var totalStreamed = 0;

        while (!ct.IsCancellationRequested)
        {
            var batchSpec = spec with { Page = page, PageSize = effectiveBatchSize };
            var result = await _crud.ListAsync(resourceKey, batchSpec, expand, ct);

            if (result.Items.Count > 0)
            {
                yield return result.Items;
                totalStreamed += result.Items.Count;
            }

            if (result.Items.Count < effectiveBatchSize || totalStreamed >= result.Total)
                break;

            page++;
        }

        sw.Stop();
        DataSurfaceTracing.RecordSuccess(activity, totalStreamed);
        _metrics?.RecordOperation(resourceKey, CrudOperation.List, sw.Elapsed.TotalMilliseconds, totalStreamed);

        _logger.LogDebug("Batch stream for {Resource} completed: {Count} items in {ElapsedMs}ms",
            resourceKey, totalStreamed, sw.ElapsedMilliseconds);
    }
}
