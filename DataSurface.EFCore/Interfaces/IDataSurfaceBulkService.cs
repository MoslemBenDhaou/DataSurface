using DataSurface.EFCore.Contracts;

namespace DataSurface.EFCore.Interfaces;

/// <summary>
/// Provides bulk CRUD operations for batch processing.
/// </summary>
/// <remarks>
/// <para>
/// Bulk operations allow multiple create, update, and delete operations in a single request,
/// improving performance for batch data processing scenarios.
/// </para>
/// <code>
/// var spec = new BulkOperationSpec
/// {
///     Create = [new JsonObject { ["name"] = "User 1" }, new JsonObject { ["name"] = "User 2" }],
///     Update = [new BulkUpdateItem { Id = 5, Patch = new JsonObject { ["name"] = "Updated" } }],
///     Delete = [10, 11, 12]
/// };
/// 
/// var result = await bulkService.ExecuteAsync("User", spec);
/// </code>
/// </remarks>
public interface IDataSurfaceBulkService
{
    /// <summary>
    /// Executes a bulk operation.
    /// </summary>
    /// <param name="resourceKey">The resource key.</param>
    /// <param name="spec">The bulk operation specification.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The result of the bulk operation.</returns>
    Task<BulkOperationResult> ExecuteAsync(string resourceKey, BulkOperationSpec spec, CancellationToken ct = default);
}
