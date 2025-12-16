using Microsoft.Extensions.DependencyInjection;
using System.Text.Json.Nodes;
using DataSurface.EFCore.Context;

namespace DataSurface.Dynamic.Hooks;

/// <summary>
/// Dispatches <see cref="ICrudHookResource"/> hooks for a specific resource key.
/// </summary>
public sealed class CrudResourceHookDispatcher
{
    private readonly IServiceProvider _sp;
    private readonly Lazy<IReadOnlyList<ICrudHookResource>> _allHooks;

    /// <summary>
    /// Creates a new dispatcher.
    /// </summary>
    /// <param name="sp">The service provider used to resolve hook implementations.</param>
    public CrudResourceHookDispatcher(IServiceProvider sp)
    {
        _sp = sp;
        // Cache all hooks once at construction to avoid repeated enumeration
        _allHooks = new Lazy<IReadOnlyList<ICrudHookResource>>(() =>
            _sp.GetServices<ICrudHookResource>().OrderBy(h => h.Order).ToList());
    }

    private IEnumerable<ICrudHookResource> Hooks(string resourceKey)
        => _allHooks.Value.Where(h => h.AppliesTo(resourceKey));

    /// <summary>
    /// Invokes hooks before a create operation.
    /// </summary>
    /// <param name="resourceKey">The resource key.</param>
    /// <param name="body">The request body.</param>
    /// <param name="ctx">The hook context.</param>
    public async Task BeforeCreateAsync(string resourceKey, JsonObject body, CrudHookContext ctx)
    {
        foreach (var h in Hooks(resourceKey)) await h.BeforeCreateAsync(resourceKey, body, ctx);
    }

    /// <summary>
    /// Invokes hooks after a create operation.
    /// </summary>
    /// <param name="resourceKey">The resource key.</param>
    /// <param name="created">The created resource JSON.</param>
    /// <param name="ctx">The hook context.</param>
    public async Task AfterCreateAsync(string resourceKey, JsonObject created, CrudHookContext ctx)
    {
        foreach (var h in Hooks(resourceKey)) await h.AfterCreateAsync(resourceKey, created, ctx);
    }

    /// <summary>
    /// Invokes hooks before an update operation.
    /// </summary>
    /// <param name="resourceKey">The resource key.</param>
    /// <param name="id">The record identifier.</param>
    /// <param name="patch">The patch payload.</param>
    /// <param name="ctx">The hook context.</param>
    public async Task BeforeUpdateAsync(string resourceKey, object id, JsonObject patch, CrudHookContext ctx)
    {
        foreach (var h in Hooks(resourceKey)) await h.BeforeUpdateAsync(resourceKey, id, patch, ctx);
    }

    /// <summary>
    /// Invokes hooks after an update operation.
    /// </summary>
    /// <param name="resourceKey">The resource key.</param>
    /// <param name="id">The record identifier.</param>
    /// <param name="updated">The updated resource JSON.</param>
    /// <param name="ctx">The hook context.</param>
    public async Task AfterUpdateAsync(string resourceKey, object id, JsonObject updated, CrudHookContext ctx)
    {
        foreach (var h in Hooks(resourceKey)) await h.AfterUpdateAsync(resourceKey, id, updated, ctx);
    }

    /// <summary>
    /// Invokes hooks before a delete operation.
    /// </summary>
    /// <param name="resourceKey">The resource key.</param>
    /// <param name="id">The record identifier.</param>
    /// <param name="ctx">The hook context.</param>
    public async Task BeforeDeleteAsync(string resourceKey, object id, CrudHookContext ctx)
    {
        foreach (var h in Hooks(resourceKey)) await h.BeforeDeleteAsync(resourceKey, id, ctx);
    }

    /// <summary>
    /// Invokes hooks after a delete operation.
    /// </summary>
    /// <param name="resourceKey">The resource key.</param>
    /// <param name="id">The record identifier.</param>
    /// <param name="ctx">The hook context.</param>
    public async Task AfterDeleteAsync(string resourceKey, object id, CrudHookContext ctx)
    {
        foreach (var h in Hooks(resourceKey)) await h.AfterDeleteAsync(resourceKey, id, ctx);
    }

    /// <summary>
    /// Invokes hooks after a read operation.
    /// </summary>
    /// <param name="resourceKey">The resource key.</param>
    /// <param name="id">The record identifier.</param>
    /// <param name="obj">The read resource JSON.</param>
    /// <param name="ctx">The hook context.</param>
    public async Task AfterReadAsync(string resourceKey, object id, JsonObject obj, CrudHookContext ctx)
    {
        foreach (var h in Hooks(resourceKey)) await h.AfterReadAsync(resourceKey, id, obj, ctx);
    }
}
