using System.Text.Json.Nodes;
using DataSurface.EFCore.Contracts;

namespace DataSurface.EFCore.Interfaces;

/// <summary>
/// Provides untyped CRUD operations for DataSurface resources.
/// </summary>
public interface IDataSurfaceCrudService
{
    /// <summary>
    /// Lists resources for the given <paramref name="resourceKey"/>.
    /// </summary>
    /// <param name="resourceKey">The resource key to list.</param>
    /// <param name="spec">Query parameters for paging, sorting and filtering.</param>
    /// <param name="expand">Optional expand specification.</param>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>A paged list result of JSON objects.</returns>
    Task<PagedResult<JsonObject>> ListAsync(
        string resourceKey,
        QuerySpec spec,
        ExpandSpec? expand = null,
        CancellationToken ct = default);

    /// <summary>
    /// Gets a single resource instance by its identifier.
    /// </summary>
    /// <param name="resourceKey">The resource key to read.</param>
    /// <param name="id">The record identifier.</param>
    /// <param name="expand">Optional expand specification.</param>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>The resource as JSON if found; otherwise <c>null</c>.</returns>
    Task<JsonObject?> GetAsync(
        string resourceKey,
        object id,
        ExpandSpec? expand = null,
        CancellationToken ct = default);

    /// <summary>
    /// Creates a resource instance.
    /// </summary>
    /// <param name="resourceKey">The resource key to create.</param>
    /// <param name="body">The request body.</param>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>The created resource as JSON.</returns>
    Task<JsonObject> CreateAsync(
        string resourceKey,
        JsonObject body,
        CancellationToken ct = default);

    /// <summary>
    /// Updates a resource instance.
    /// </summary>
    /// <param name="resourceKey">The resource key to update.</param>
    /// <param name="id">The record identifier.</param>
    /// <param name="patch">The patch payload.</param>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>The updated resource as JSON.</returns>
    Task<JsonObject> UpdateAsync(
        string resourceKey,
        object id,
        JsonObject patch,
        CancellationToken ct = default);

    /// <summary>
    /// Deletes a resource instance.
    /// </summary>
    /// <param name="resourceKey">The resource key to delete from.</param>
    /// <param name="id">The record identifier.</param>
    /// <param name="deleteSpec">Optional delete options.</param>
    /// <param name="ct">A cancellation token.</param>
    Task DeleteAsync(
        string resourceKey,
        object id,
        CrudDeleteSpec? deleteSpec = null,
        CancellationToken ct = default);
}

/// <summary>
/// Provides typed CRUD operations for a specific entity type.
/// </summary>
/// <typeparam name="TEntity">The entity CLR type.</typeparam>
/// <typeparam name="TKey">The entity key type.</typeparam>
public interface IDataSurfaceCrudService<TEntity, TKey>
    where TEntity : class
{
    /// <summary>
    /// Lists entities.
    /// </summary>
    /// <param name="spec">Query parameters for paging, sorting and filtering.</param>
    /// <param name="expand">Optional expand specification.</param>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>A paged list result of JSON objects.</returns>
    Task<PagedResult<JsonObject>> ListAsync(QuerySpec spec, ExpandSpec? expand = null, CancellationToken ct = default);

    /// <summary>
    /// Gets a single entity by its key.
    /// </summary>
    /// <param name="id">The entity identifier.</param>
    /// <param name="expand">Optional expand specification.</param>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>The resource as JSON if found; otherwise <c>null</c>.</returns>
    Task<JsonObject?> GetAsync(TKey id, ExpandSpec? expand = null, CancellationToken ct = default);

    /// <summary>
    /// Creates an entity.
    /// </summary>
    /// <param name="body">The request body.</param>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>The created entity as JSON.</returns>
    Task<JsonObject> CreateAsync(JsonObject body, CancellationToken ct = default);

    /// <summary>
    /// Updates an entity.
    /// </summary>
    /// <param name="id">The entity identifier.</param>
    /// <param name="patch">The patch payload.</param>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>The updated entity as JSON.</returns>
    Task<JsonObject> UpdateAsync(TKey id, JsonObject patch, CancellationToken ct = default);

    /// <summary>
    /// Deletes an entity.
    /// </summary>
    /// <param name="id">The entity identifier.</param>
    /// <param name="deleteSpec">Optional delete options.</param>
    /// <param name="ct">A cancellation token.</param>
    Task DeleteAsync(TKey id, CrudDeleteSpec? deleteSpec = null, CancellationToken ct = default);
}
