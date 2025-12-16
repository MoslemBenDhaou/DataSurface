using System.Text.Json.Nodes;
using DataSurface.EFCore.Context;

namespace DataSurface.Dynamic.Hooks;

/// <summary>
/// Hook interface for resource-key specific behaviors in the dynamic CRUD service.
/// </summary>
public interface ICrudHookResource
{
    /// <summary>
    /// Gets the execution order for this hook (lower runs first).
    /// </summary>
    int Order => 0;

    /// <summary>
    /// Determines whether this hook applies to the provided <paramref name="resourceKey"/>.
    /// </summary>
    /// <param name="resourceKey">The resource key being processed.</param>
    /// <returns><c>true</c> if the hook applies; otherwise <c>false</c>.</returns>
    bool AppliesTo(string resourceKey);

    /// <summary>
    /// Called before a create operation.
    /// </summary>
    /// <param name="resourceKey">The resource key.</param>
    /// <param name="body">The request body.</param>
    /// <param name="ctx">The hook context.</param>
    Task BeforeCreateAsync(string resourceKey, JsonObject body, CrudHookContext ctx) => Task.CompletedTask;

    /// <summary>
    /// Called after a create operation.
    /// </summary>
    /// <param name="resourceKey">The resource key.</param>
    /// <param name="created">The created resource JSON.</param>
    /// <param name="ctx">The hook context.</param>
    Task AfterCreateAsync(string resourceKey, JsonObject created, CrudHookContext ctx) => Task.CompletedTask;

    /// <summary>
    /// Called before an update operation.
    /// </summary>
    /// <param name="resourceKey">The resource key.</param>
    /// <param name="id">The record identifier.</param>
    /// <param name="patch">The patch payload.</param>
    /// <param name="ctx">The hook context.</param>
    Task BeforeUpdateAsync(string resourceKey, object id, JsonObject patch, CrudHookContext ctx) => Task.CompletedTask;

    /// <summary>
    /// Called after an update operation.
    /// </summary>
    /// <param name="resourceKey">The resource key.</param>
    /// <param name="id">The record identifier.</param>
    /// <param name="updated">The updated resource JSON.</param>
    /// <param name="ctx">The hook context.</param>
    Task AfterUpdateAsync(string resourceKey, object id, JsonObject updated, CrudHookContext ctx) => Task.CompletedTask;

    /// <summary>
    /// Called before a delete operation.
    /// </summary>
    /// <param name="resourceKey">The resource key.</param>
    /// <param name="id">The record identifier.</param>
    /// <param name="ctx">The hook context.</param>
    Task BeforeDeleteAsync(string resourceKey, object id, CrudHookContext ctx) => Task.CompletedTask;

    /// <summary>
    /// Called after a delete operation.
    /// </summary>
    /// <param name="resourceKey">The resource key.</param>
    /// <param name="id">The record identifier.</param>
    /// <param name="ctx">The hook context.</param>
    Task AfterDeleteAsync(string resourceKey, object id, CrudHookContext ctx) => Task.CompletedTask;

    /// <summary>
    /// Called after a read operation.
    /// </summary>
    /// <param name="resourceKey">The resource key.</param>
    /// <param name="id">The record identifier.</param>
    /// <param name="obj">The read resource JSON.</param>
    /// <param name="ctx">The hook context.</param>
    Task AfterReadAsync(string resourceKey, object id, JsonObject obj, CrudHookContext ctx) => Task.CompletedTask;
}
