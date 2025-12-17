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
                    errors.Add(new BulkOperationError
                    {
                        Operation = "Create",
                        Index = i,
                        Message = ex.Message
                    });

                    if (spec.StopOnError)
                    {
                        if (transaction is not null) await transaction.RollbackAsync(ct);
                        break;
                    }
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
                        errors.Add(new BulkOperationError
                        {
                            Operation = "Update",
                            Index = i,
                            Id = item.Id,
                            Message = ex.Message
                        });

                        if (spec.StopOnError)
                        {
                            if (transaction is not null) await transaction.RollbackAsync(ct);
                            break;
                        }
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
                        errors.Add(new BulkOperationError
                        {
                            Operation = "Delete",
                            Index = i,
                            Id = id,
                            Message = ex.Message
                        });

                        if (spec.StopOnError)
                        {
                            if (transaction is not null) await transaction.RollbackAsync(ct);
                            break;
                        }
                    }
                }
            }

            if (transaction is not null && errors.Count == 0)
                await transaction.CommitAsync(ct);
        }
        catch (Exception ex)
        {
            if (transaction is not null)
                await transaction.RollbackAsync(ct);

            errors.Add(new BulkOperationError
            {
                Operation = "Transaction",
                Index = -1,
                Message = ex.Message
            });
        }
        finally
        {
            if (transaction is not null)
                await transaction.DisposeAsync();
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
