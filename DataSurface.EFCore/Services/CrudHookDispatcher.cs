using DataSurface.EFCore.Context;
using DataSurface.EFCore.Interfaces;
using Microsoft.Extensions.DependencyInjection;

namespace DataSurface.EFCore.Services;

/// <summary>
/// Dispatches registered CRUD hooks in a deterministic order.
/// </summary>
public sealed class CrudHookDispatcher
{
    private readonly IServiceProvider _sp;

    /// <summary>
    /// Creates a new dispatcher.
    /// </summary>
    /// <param name="sp">The service provider used to resolve hook implementations.</param>
    public CrudHookDispatcher(IServiceProvider sp)
    {
        _sp = sp;
    }

    /// <summary>
    /// Invokes all global hooks before an operation.
    /// </summary>
    /// <param name="ctx">The hook context.</param>
    public async Task BeforeGlobalAsync(CrudHookContext ctx)
    {
        foreach (var h in ResolveGlobalHooks()) await h.BeforeAsync(ctx);
    }

    /// <summary>
    /// Invokes all global hooks after an operation.
    /// </summary>
    /// <param name="ctx">The hook context.</param>
    public async Task AfterGlobalAsync(CrudHookContext ctx)
    {
        foreach (var h in ResolveGlobalHooks()) await h.AfterAsync(ctx);
    }

    private IReadOnlyList<ICrudHook> ResolveGlobalHooks()
        => _sp.GetServices<ICrudHook>().OrderBy(h => h.Order).ToList();

    /// <summary>
    /// Invokes typed hooks before a create operation.
    /// </summary>
    /// <typeparam name="TEntity">The entity type.</typeparam>
    /// <param name="entity">The entity instance being created.</param>
    /// <param name="body">The JSON body used to create the entity.</param>
    /// <param name="ctx">The hook context.</param>
    public async Task BeforeCreateAsync<TEntity>(TEntity entity, System.Text.Json.Nodes.JsonObject body, CrudHookContext ctx)
    {
        var hooks = _sp.GetServices<ICrudHook<TEntity>>().OrderBy(h => h.Order);
        foreach (var h in hooks) await h.BeforeCreateAsync(entity, body, ctx);
    }

    /// <summary>
    /// Invokes typed hooks after a create operation.
    /// </summary>
    /// <typeparam name="TEntity">The entity type.</typeparam>
    /// <param name="entity">The created entity instance.</param>
    /// <param name="ctx">The hook context.</param>
    public async Task AfterCreateAsync<TEntity>(TEntity entity, CrudHookContext ctx)
    {
        var hooks = _sp.GetServices<ICrudHook<TEntity>>().OrderBy(h => h.Order);
        foreach (var h in hooks) await h.AfterCreateAsync(entity, ctx);
    }

    /// <summary>
    /// Invokes typed hooks before an update operation.
    /// </summary>
    /// <typeparam name="TEntity">The entity type.</typeparam>
    /// <param name="entity">The entity instance being updated.</param>
    /// <param name="patch">The JSON patch payload.</param>
    /// <param name="ctx">The hook context.</param>
    public async Task BeforeUpdateAsync<TEntity>(TEntity entity, System.Text.Json.Nodes.JsonObject patch, CrudHookContext ctx)
    {
        var hooks = _sp.GetServices<ICrudHook<TEntity>>().OrderBy(h => h.Order);
        foreach (var h in hooks) await h.BeforeUpdateAsync(entity, patch, ctx);
    }

    /// <summary>
    /// Invokes typed hooks after an update operation.
    /// </summary>
    /// <typeparam name="TEntity">The entity type.</typeparam>
    /// <param name="entity">The updated entity instance.</param>
    /// <param name="ctx">The hook context.</param>
    public async Task AfterUpdateAsync<TEntity>(TEntity entity, CrudHookContext ctx)
    {
        var hooks = _sp.GetServices<ICrudHook<TEntity>>().OrderBy(h => h.Order);
        foreach (var h in hooks) await h.AfterUpdateAsync(entity, ctx);
    }

    /// <summary>
    /// Invokes typed hooks before a delete operation.
    /// </summary>
    /// <typeparam name="TEntity">The entity type.</typeparam>
    /// <param name="entity">The entity instance being deleted.</param>
    /// <param name="ctx">The hook context.</param>
    public async Task BeforeDeleteAsync<TEntity>(TEntity entity, CrudHookContext ctx)
    {
        var hooks = _sp.GetServices<ICrudHook<TEntity>>().OrderBy(h => h.Order);
        foreach (var h in hooks) await h.BeforeDeleteAsync(entity, ctx);
    }

    /// <summary>
    /// Invokes typed hooks after a delete operation.
    /// </summary>
    /// <typeparam name="TEntity">The entity type.</typeparam>
    /// <param name="entity">The deleted entity instance.</param>
    /// <param name="ctx">The hook context.</param>
    public async Task AfterDeleteAsync<TEntity>(TEntity entity, CrudHookContext ctx)
    {
        var hooks = _sp.GetServices<ICrudHook<TEntity>>().OrderBy(h => h.Order);
        foreach (var h in hooks) await h.AfterDeleteAsync(entity, ctx);
    }

    /// <summary>
    /// Invokes typed hooks after an entity has been read.
    /// </summary>
    /// <typeparam name="TEntity">The entity type.</typeparam>
    /// <param name="entity">The entity instance that was read.</param>
    /// <param name="ctx">The hook context.</param>
    public async Task AfterReadAsync<TEntity>(TEntity entity, CrudHookContext ctx)
    {
        var hooks = _sp.GetServices<ICrudHook<TEntity>>().OrderBy(h => h.Order);
        foreach (var h in hooks) await h.AfterReadAsync(entity, ctx);
    }
}
