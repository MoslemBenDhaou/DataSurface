using System.Diagnostics;
using DataSurface.Core.Enums;

namespace DataSurface.EFCore.Observability;

/// <summary>
/// Provides distributed tracing for DataSurface CRUD operations using <see cref="Activity"/>.
/// </summary>
/// <remarks>
/// <para>
/// Tracing is implemented using <see cref="System.Diagnostics.Activity"/> which is compatible with
/// OpenTelemetry, Application Insights, Jaeger, Zipkin, and other distributed tracing systems.
/// </para>
/// <para>
/// To enable tracing, configure your OpenTelemetry provider to listen to the <c>DataSurface</c> source:
/// </para>
/// <code>
/// builder.Services.AddOpenTelemetry()
///     .WithTracing(tracing => tracing.AddSource("DataSurface"));
/// </code>
/// </remarks>
public static class DataSurfaceTracing
{
    /// <summary>
    /// The activity source name used for all DataSurface traces.
    /// </summary>
    public const string SourceName = "DataSurface";

    private static readonly ActivitySource Source = new(SourceName, "1.0.0");

    /// <summary>
    /// Starts a new activity/span for a CRUD operation.
    /// </summary>
    /// <param name="resourceKey">The resource key.</param>
    /// <param name="operation">The CRUD operation.</param>
    /// <param name="entityId">Optional entity ID for single-entity operations.</param>
    /// <returns>An activity that should be disposed when the operation completes.</returns>
    public static Activity? StartOperation(string resourceKey, CrudOperation operation, object? entityId = null)
    {
        var activityName = $"DataSurface.{operation} {resourceKey}";
        var activity = Source.StartActivity(activityName, ActivityKind.Internal);

        if (activity is null)
            return null;

        activity.SetTag("datasurface.resource", resourceKey);
        activity.SetTag("datasurface.operation", operation.ToString().ToLowerInvariant());

        if (entityId is not null)
            activity.SetTag("datasurface.entity_id", entityId.ToString());

        return activity;
    }

    /// <summary>
    /// Records success on an activity.
    /// </summary>
    /// <param name="activity">The activity.</param>
    /// <param name="rowsAffected">Number of rows affected.</param>
    public static void RecordSuccess(Activity? activity, int rowsAffected = 1)
    {
        if (activity is null) return;

        activity.SetTag("datasurface.rows_affected", rowsAffected);
        activity.SetStatus(ActivityStatusCode.Ok);
    }

    /// <summary>
    /// Records an error on an activity.
    /// </summary>
    /// <param name="activity">The activity.</param>
    /// <param name="exception">The exception that occurred.</param>
    public static void RecordError(Activity? activity, Exception exception)
    {
        if (activity is null) return;

        activity.SetStatus(ActivityStatusCode.Error, exception.Message);
        activity.SetTag("error.type", exception.GetType().Name);
        activity.SetTag("error.message", exception.Message);

        // Add exception event for detailed tracing
        activity.AddEvent(new ActivityEvent("exception", tags: new ActivityTagsCollection
        {
            { "exception.type", exception.GetType().FullName },
            { "exception.message", exception.Message },
            { "exception.stacktrace", exception.StackTrace }
        }));
    }

    /// <summary>
    /// Adds query parameters to an activity for list operations.
    /// </summary>
    /// <param name="activity">The activity.</param>
    /// <param name="page">The page number.</param>
    /// <param name="pageSize">The page size.</param>
    /// <param name="filterCount">Number of filters applied.</param>
    /// <param name="sortCount">Number of sort fields.</param>
    public static void AddQueryParameters(Activity? activity, int page, int pageSize, int filterCount = 0, int sortCount = 0)
    {
        if (activity is null) return;

        activity.SetTag("datasurface.query.page", page);
        activity.SetTag("datasurface.query.page_size", pageSize);
        
        if (filterCount > 0)
            activity.SetTag("datasurface.query.filter_count", filterCount);
        
        if (sortCount > 0)
            activity.SetTag("datasurface.query.sort_count", sortCount);
    }

    /// <summary>
    /// Adds expand information to an activity.
    /// </summary>
    /// <param name="activity">The activity.</param>
    /// <param name="expandFields">The fields being expanded.</param>
    public static void AddExpandInfo(Activity? activity, IEnumerable<string>? expandFields)
    {
        if (activity is null || expandFields is null) return;

        var fields = string.Join(",", expandFields);
        if (!string.IsNullOrEmpty(fields))
            activity.SetTag("datasurface.query.expand", fields);
    }
}
