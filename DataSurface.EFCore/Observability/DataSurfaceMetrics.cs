using System.Diagnostics;
using System.Diagnostics.Metrics;
using DataSurface.Core.Enums;

namespace DataSurface.EFCore.Observability;

/// <summary>
/// Provides OpenTelemetry-compatible metrics for DataSurface CRUD operations.
/// </summary>
/// <remarks>
/// <para>
/// Metrics are exposed via <see cref="System.Diagnostics.Metrics"/> and are compatible with
/// OpenTelemetry, Prometheus, and other metrics collectors.
/// </para>
/// <para>
/// To enable metrics collection, configure your OpenTelemetry provider to listen to the
/// <c>DataSurface</c> meter:
/// </para>
/// <code>
/// builder.Services.AddOpenTelemetry()
///     .WithMetrics(metrics => metrics.AddMeter("DataSurface"));
/// </code>
/// </remarks>
public sealed class DataSurfaceMetrics : IDisposable
{
    /// <summary>
    /// The meter name used for all DataSurface metrics.
    /// </summary>
    public const string MeterName = "DataSurface";

    private readonly Meter _meter;
    private readonly Counter<long> _operationCounter;
    private readonly Counter<long> _errorCounter;
    private readonly Histogram<double> _operationDuration;
    private readonly Counter<long> _rowsAffectedCounter;

    /// <summary>
    /// Creates a new metrics instance.
    /// </summary>
    public DataSurfaceMetrics()
    {
        _meter = new Meter(MeterName, "1.0.0");

        _operationCounter = _meter.CreateCounter<long>(
            "datasurface.operations",
            unit: "{operation}",
            description: "Total number of CRUD operations performed");

        _errorCounter = _meter.CreateCounter<long>(
            "datasurface.errors",
            unit: "{error}",
            description: "Total number of failed CRUD operations");

        _operationDuration = _meter.CreateHistogram<double>(
            "datasurface.operation.duration",
            unit: "ms",
            description: "Duration of CRUD operations in milliseconds");

        _rowsAffectedCounter = _meter.CreateCounter<long>(
            "datasurface.rows_affected",
            unit: "{row}",
            description: "Total number of rows affected by CRUD operations");
    }

    /// <summary>
    /// Records a successful CRUD operation.
    /// </summary>
    /// <param name="resourceKey">The resource key.</param>
    /// <param name="operation">The CRUD operation.</param>
    /// <param name="durationMs">The duration in milliseconds.</param>
    /// <param name="rowsAffected">Number of rows affected (for list operations).</param>
    public void RecordOperation(string resourceKey, CrudOperation operation, double durationMs, int rowsAffected = 1)
    {
        var tags = new TagList
        {
            { "resource", resourceKey },
            { "operation", operation.ToString().ToLowerInvariant() }
        };

        _operationCounter.Add(1, tags);
        _operationDuration.Record(durationMs, tags);
        
        if (rowsAffected > 0)
            _rowsAffectedCounter.Add(rowsAffected, tags);
    }

    /// <summary>
    /// Records a failed CRUD operation.
    /// </summary>
    /// <param name="resourceKey">The resource key.</param>
    /// <param name="operation">The CRUD operation.</param>
    /// <param name="errorType">The type of error.</param>
    /// <param name="durationMs">The duration in milliseconds.</param>
    public void RecordError(string resourceKey, CrudOperation operation, string errorType, double durationMs)
    {
        var tags = new TagList
        {
            { "resource", resourceKey },
            { "operation", operation.ToString().ToLowerInvariant() },
            { "error_type", errorType }
        };

        _errorCounter.Add(1, tags);
        _operationDuration.Record(durationMs, tags);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _meter.Dispose();
    }
}
