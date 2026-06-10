using System.Text.Json.Nodes;
using DataSurface.Core.Enums;
using DataSurface.EFCore.Contracts;
using DataSurface.EFCore.Interfaces;
using DataSurface.EFCore.Observability;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging;

namespace DataSurface.EFCore.Services;

/// <summary>
/// Entity Framework Core implementation of <see cref="IDataSurfaceBulkService"/>.
/// </summary>
public sealed class EfDataSurfaceBulkService : IDataSurfaceBulkService
{
    private readonly DbContext _db;
    private readonly IDataSurfaceCrudService _crud;
    private readonly IResourceContractProvider _contracts;
    private readonly ILogger<EfDataSurfaceBulkService> _logger;
    private readonly DataSurfaceMetrics? _metrics;

    /// <summary>
    /// Creates a new bulk service instance.
    /// </summary>
    public EfDataSurfaceBulkService(
        DbContext db,
        IDataSurfaceCrudService crud,
        IResourceContractProvider contracts,
        ILogger<EfDataSurfaceBulkService> logger,
        DataSurfaceMetrics? metrics = null)
    {
        _db = db;
        _crud = crud;
        _contracts = contracts;
        _logger = logger;
        _metrics = metrics;
    }

    /// <inheritdoc />
    public async Task<BulkOperationResult> ExecuteAsync(string resourceKey, BulkOperationSpec spec, CancellationToken ct = default)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        using var activity = DataSurfaceTracing.StartOperation(resourceKey, CrudOperation.Create);
        activity?.SetTag("datasurface.bulk", true);
        activity?.SetTag("datasurface.bulk.create_count", spec.Create.Count);
        activity?.SetTag("datasurface.bulk.update_count", spec.Update.Count);
        activity?.SetTag("datasurface.bulk.delete_count", spec.Delete.Count);

        _logger.LogDebug("Bulk operation on {Resource}: {CreateCount} creates, {UpdateCount} updates, {DeleteCount} deletes",
            resourceKey, spec.Create.Count, spec.Update.Count, spec.Delete.Count);

        // Validate resource exists
        _ = _contracts.GetByResourceKey(resourceKey);

        var created = new List<JsonObject>();
        var updated = new List<JsonObject>();
        var deletedCount = 0;
        var errors = new List<BulkOperationError>();

        IDbContextTransaction? transaction = null;
        if (spec.UseTransaction)
            transaction = await _db.Database.BeginTransactionAsync(ct);

        // Inside a transaction, webhooks and audit entries must not fire per item: a rollback
        // would leave external consumers with events for rows that never persisted. Buffer them
        // and flush only after a successful commit.
        var deferredEvents = transaction is not null && _crud is EfDataSurfaceCrudService efCrud
            ? efCrud.BeginDeferredEvents()
            : null;

        try
        {
            // Process creates
            for (var i = 0; i < spec.Create.Count; i++)
            {
                try
                {
                    var result = await _crud.CreateAsync(resourceKey, spec.Create[i], ct);
                    created.Add(result);
                }
                catch (Exception ex)
                {
                    // A failed SaveChanges leaves the failed entity tracked (Added/Modified);
                    // without clearing, every subsequent item re-attempts it and fails too.
                    _db.ChangeTracker.Clear();

                    errors.Add(new BulkOperationError
                    {
                        Operation = "Create",
                        Index = i,
                        Message = ex.Message
                    });

                    if (spec.StopOnError)
                        break;
                }
            }

            // Process updates
            if (errors.Count == 0 || !spec.StopOnError)
            {
                for (var i = 0; i < spec.Update.Count; i++)
                {
                    var item = spec.Update[i];
                    try
                    {
                        var result = await _crud.UpdateAsync(resourceKey, item.Id, item.Patch, ct);
                        updated.Add(result);
                    }
                    catch (Exception ex)
                    {
                        _db.ChangeTracker.Clear();

                        errors.Add(new BulkOperationError
                        {
                            Operation = "Update",
                            Index = i,
                            Id = item.Id,
                            Message = ex.Message
                        });

                        if (spec.StopOnError)
                            break;
                    }
                }
            }

            // Process deletes
            if (errors.Count == 0 || !spec.StopOnError)
            {
                for (var i = 0; i < spec.Delete.Count; i++)
                {
                    var id = spec.Delete[i];
                    try
                    {
                        await _crud.DeleteAsync(resourceKey, id, deleteSpec: null, ct);
                        deletedCount++;
                    }
                    catch (Exception ex)
                    {
                        _db.ChangeTracker.Clear();

                        errors.Add(new BulkOperationError
                        {
                            Operation = "Delete",
                            Index = i,
                            Id = id,
                            Message = ex.Message
                        });

                        if (spec.StopOnError)
                            break;
                    }
                }
            }

            if (transaction is not null)
            {
                if (errors.Count == 0)
                {
                    await transaction.CommitAsync(ct);

                    if (deferredEvents is not null)
                        await deferredEvents.FlushAsync(ct);
                }
                else
                {
                    await transaction.RollbackAsync(ct);
                }
            }
        }
        catch (Exception ex)
        {
            if (transaction is not null)
            {
                // Defensive: the transaction may already have been rolled back (or completed) by the
                // commit/rollback block above; don't let a second rollback mask the real error.
                try { await transaction.RollbackAsync(ct); } catch { /* already completed */ }
            }

            errors.Add(new BulkOperationError
            {
                Operation = "Transaction",
                Index = -1,
                Message = ex.Message
            });
        }
        finally
        {
            // Disposing an unflushed scope discards the buffered events (rollback semantics).
            deferredEvents?.Dispose();

            if (transaction is not null)
                await transaction.DisposeAsync();
        }

        // If a transaction was used and any operation failed, the whole batch was rolled back,
        // so the per-item successes never persisted. Clear them so the response does not report
        // created/updated/deleted records that no longer exist.
        if (spec.UseTransaction && errors.Count > 0)
        {
            created.Clear();
            updated.Clear();
            deletedCount = 0;
        }

        sw.Stop();
        var totalOps = created.Count + updated.Count + deletedCount;
        
        DataSurfaceTracing.RecordSuccess(activity, totalOps);
        _metrics?.RecordOperation(resourceKey, CrudOperation.Create, sw.Elapsed.TotalMilliseconds, totalOps);

        _logger.LogInformation(
            "Bulk operation on {Resource} completed in {ElapsedMs}ms: {Created} created, {Updated} updated, {Deleted} deleted, {Errors} errors",
            resourceKey, sw.ElapsedMilliseconds, created.Count, updated.Count, deletedCount, errors.Count);

        return new BulkOperationResult
        {
            Created = created,
            Updated = updated,
            DeletedCount = deletedCount,
            Errors = errors
        };
    }
}
