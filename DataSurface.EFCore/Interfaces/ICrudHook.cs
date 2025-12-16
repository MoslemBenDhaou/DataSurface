using System.Text.Json.Nodes;
using DataSurface.EFCore.Context;

namespace DataSurface.EFCore.Interfaces;

// Global hook (runs for all resources)
/// <summary>
/// Global CRUD hook that runs for all resources.
/// </summary>
public interface ICrudHook
{
    /// <summary>
    /// Gets the execution order for this hook (lower runs first).
    /// </summary>
    int Order => 0;

    /// <summary>
    /// Called before a CRUD operation is executed.
    /// </summary>
    /// <param name="ctx">The hook context.</param>
    /// <returns>A task that completes when the hook has finished.</returns>
    Task BeforeAsync(CrudHookContext ctx) => Task.CompletedTask;

    /// <summary>
    /// Called after a CRUD operation has completed.
    /// </summary>
    /// <param name="ctx">The hook context.</param>
    /// <returns>A task that completes when the hook has finished.</returns>
    Task AfterAsync(CrudHookContext ctx) => Task.CompletedTask;
}

// Typed hook (runs only for TEntity resource)
/// <summary>
/// CRUD hook that runs only for a specific entity type.
/// </summary>
/// <typeparam name="TEntity">The entity type.</typeparam>
public interface ICrudHook<TEntity>
{
    /// <summary>
    /// Gets the execution order for this hook (lower runs first).
    /// </summary>
    int Order => 0;

    /// <summary>
    /// Called before a create operation.
    /// </summary>
    /// <param name="entity">The entity instance being created.</param>
    /// <param name="body">The JSON body used to create the entity.</param>
    /// <param name="ctx">The hook context.</param>
    /// <returns>A task that completes when the hook has finished.</returns>
    Task BeforeCreateAsync(TEntity entity, JsonObject body, CrudHookContext ctx) => Task.CompletedTask;

    /// <summary>
    /// Called after a create operation.
    /// </summary>
    /// <param name="entity">The created entity instance.</param>
    /// <param name="ctx">The hook context.</param>
    /// <returns>A task that completes when the hook has finished.</returns>
    Task AfterCreateAsync(TEntity entity, CrudHookContext ctx) => Task.CompletedTask;

    /// <summary>
    /// Called before an update operation.
    /// </summary>
    /// <param name="entity">The entity instance being updated.</param>
    /// <param name="patch">The JSON patch payload.</param>
    /// <param name="ctx">The hook context.</param>
    /// <returns>A task that completes when the hook has finished.</returns>
    Task BeforeUpdateAsync(TEntity entity, JsonObject patch, CrudHookContext ctx) => Task.CompletedTask;

    /// <summary>
    /// Called after an update operation.
    /// </summary>
    /// <param name="entity">The updated entity instance.</param>
    /// <param name="ctx">The hook context.</param>
    /// <returns>A task that completes when the hook has finished.</returns>
    Task AfterUpdateAsync(TEntity entity, CrudHookContext ctx) => Task.CompletedTask;

    /// <summary>
    /// Called before a delete operation.
    /// </summary>
    /// <param name="entity">The entity instance being deleted.</param>
    /// <param name="ctx">The hook context.</param>
    /// <returns>A task that completes when the hook has finished.</returns>
    Task BeforeDeleteAsync(TEntity entity, CrudHookContext ctx) => Task.CompletedTask;

    /// <summary>
    /// Called after a delete operation.
    /// </summary>
    /// <param name="entity">The deleted entity instance.</param>
    /// <param name="ctx">The hook context.</param>
    /// <returns>A task that completes when the hook has finished.</returns>
    Task AfterDeleteAsync(TEntity entity, CrudHookContext ctx) => Task.CompletedTask;

    /// <summary>
    /// Called after an entity has been read.
    /// </summary>
    /// <param name="entity">The entity instance that was read.</param>
    /// <param name="ctx">The hook context.</param>
    /// <returns>A task that completes when the hook has finished.</returns>
    Task AfterReadAsync(TEntity entity, CrudHookContext ctx) => Task.CompletedTask;
}
