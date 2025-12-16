using System.Collections.Concurrent;
using System.Text.Json.Nodes;
using DataSurface.Core.Contracts;
using DataSurface.Core.Enums;
using DataSurface.EFCore.Contracts;
using DataSurface.EFCore.Interfaces;
using DataSurface.EFCore.Mapper;
using Microsoft.EntityFrameworkCore;

namespace DataSurface.EFCore.Context;

// services.AddScoped<CrudHookDispatcher>();
// services.AddSingleton<CrudOverrideRegistry>();

// services.AddScoped<ICrudHook, AuditHook>();           // global
// services.AddScoped<ICrudHook<Post>, PostRulesHook>(); // typed

// var registry = app.Services.GetRequiredService<CrudOverrideRegistry>();
// registry.Override("Post", CrudOperation.Update, (UpdateOverride)MyCustomPostUpdate);

/// <summary>
/// Context passed to CRUD override delegates with access to services and helpers.
/// </summary>
public sealed class CrudServiceContext
{
    /// <summary>
    /// Gets the service provider for resolving scoped services.
    /// </summary>
    public required IServiceProvider Services { get; init; }

    /// <summary>
    /// Gets the EF Core database context.
    /// </summary>
    public required DbContext Db { get; init; }

    /// <summary>
    /// Gets the mapper used to apply JSON payloads to entities.
    /// </summary>
    public required EfCrudMapper Mapper { get; init; }

    /// <summary>
    /// Gets the query engine used to apply filtering, sorting and paging.
    /// </summary>
    public required EfCrudQueryEngine Query { get; init; }

    /// <summary>
    /// Gets the contract provider used to resolve resource contracts.
    /// </summary>
    public required IResourceContractProvider Contracts { get; init; }
}

/// <summary>
/// Delegate used to override list behavior for a resource.
/// </summary>
public delegate Task<PagedResult<JsonObject>> ListOverride(
    ResourceContract c, QuerySpec spec, ExpandSpec? expand, CrudServiceContext ctx, CancellationToken ct);

/// <summary>
/// Delegate used to override get-by-id behavior for a resource.
/// </summary>
public delegate Task<JsonObject?> GetOverride(
    ResourceContract c, object id, ExpandSpec? expand, CrudServiceContext ctx, CancellationToken ct);

/// <summary>
/// Delegate used to override create behavior for a resource.
/// </summary>
public delegate Task<JsonObject> CreateOverride(
    ResourceContract c, JsonObject body, CrudServiceContext ctx, CancellationToken ct);

/// <summary>
/// Delegate used to override update behavior for a resource.
/// </summary>
public delegate Task<JsonObject> UpdateOverride(
    ResourceContract c, object id, JsonObject patch, CrudServiceContext ctx, CancellationToken ct);

/// <summary>
/// Delegate used to override delete behavior for a resource.
/// </summary>
public delegate Task DeleteOverride(
    ResourceContract c, object id, CrudDeleteSpec? deleteSpec, CrudServiceContext ctx, CancellationToken ct);

/// <summary>
/// Registry of per-resource override delegates.
/// </summary>
public sealed class CrudOverrideRegistry
{
    private readonly ConcurrentDictionary<(string key, CrudOperation op), Delegate> _map = new(new CrudKeyComparer());

    /// <summary>
    /// Registers an override handler for the given resource and operation.
    /// </summary>
    /// <param name="resourceKey">The resource key.</param>
    /// <param name="op">The CRUD operation to override.</param>
    /// <param name="handler">The override delegate.</param>
    public void Override(string resourceKey, CrudOperation op, Delegate handler)
        => _map[(resourceKey, op)] = handler;

    /// <summary>
    /// Attempts to retrieve a previously registered override handler.
    /// </summary>
    /// <typeparam name="T">The expected delegate type.</typeparam>
    /// <param name="resourceKey">The resource key.</param>
    /// <param name="op">The CRUD operation.</param>
    /// <param name="handler">The handler if found and of the expected type.</param>
    /// <returns><c>true</c> if a handler was found; otherwise <c>false</c>.</returns>
    public bool TryGet<T>(string resourceKey, CrudOperation op, out T? handler) where T : class
    {
        if (_map.TryGetValue((resourceKey, op), out var d))
        {
            handler = d as T;
            return handler != null;
        }
        handler = null;
        return false;
    }

    private sealed class CrudKeyComparer : IEqualityComparer<(string key, CrudOperation op)>
    {
        /// <inheritdoc />
        public bool Equals((string key, CrudOperation op) x, (string key, CrudOperation op) y)
        {
            return StringComparer.OrdinalIgnoreCase.Equals(x.key, y.key) && x.op == y.op;
        }

        /// <inheritdoc />
        public int GetHashCode((string key, CrudOperation op) obj)
        {
            return HashCode.Combine(
                StringComparer.OrdinalIgnoreCase.GetHashCode(obj.key),
                obj.op
            );
        }
    }
}

