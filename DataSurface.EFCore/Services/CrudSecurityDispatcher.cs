using System.Linq.Expressions;
using System.Text.Json.Nodes;
using DataSurface.Core.Contracts;
using DataSurface.Core.Enums;
using DataSurface.EFCore.Interfaces;
using Microsoft.Extensions.DependencyInjection;

namespace DataSurface.EFCore.Services;

/// <summary>
/// Dispatches security-related operations including row-level filtering, field authorization, and audit logging.
/// </summary>
public sealed class CrudSecurityDispatcher
{
    private readonly IServiceProvider _sp;

    /// <summary>
    /// Creates a new security dispatcher.
    /// </summary>
    /// <param name="sp">The service provider for resolving security services.</param>
    public CrudSecurityDispatcher(IServiceProvider sp)
    {
        _sp = sp;
    }

    /// <summary>
    /// Applies row-level security filters to a query.
    /// </summary>
    /// <typeparam name="TEntity">The entity type.</typeparam>
    /// <param name="query">The query to filter.</param>
    /// <param name="contract">The resource contract.</param>
    /// <returns>The filtered query.</returns>
    public IQueryable<TEntity> ApplyResourceFilter<TEntity>(IQueryable<TEntity> query, ResourceContract contract)
        where TEntity : class
    {
        // Try typed filter first
        var typedFilter = _sp.GetService<IResourceFilter<TEntity>>();
        if (typedFilter is not null)
        {
            var filter = typedFilter.GetFilter(contract);
            if (filter is not null)
                query = query.Where(filter);
        }

        // Then try non-generic filters
        var globalFilters = _sp.GetServices<IResourceFilter>();
        foreach (var gf in globalFilters)
        {
            if (!gf.AppliesTo.Contains(typeof(TEntity))) continue;

            var lambda = gf.GetFilter(typeof(TEntity), contract);
            if (lambda is Expression<Func<TEntity, bool>> expr)
                query = query.Where(expr);
        }

        return query;
    }

    /// <summary>
    /// Applies row-level security filters to a non-generic query.
    /// </summary>
    /// <param name="query">The query to filter.</param>
    /// <param name="entityType">The entity type.</param>
    /// <param name="contract">The resource contract.</param>
    /// <returns>The filtered query.</returns>
    public IQueryable ApplyResourceFilter(IQueryable query, Type entityType, ResourceContract contract)
    {
        // Use reflection to call the generic version
        var method = typeof(CrudSecurityDispatcher)
            .GetMethod(nameof(ApplyResourceFilterInternal), 
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
            .MakeGenericMethod(entityType);

        return (IQueryable)method.Invoke(this, [query, contract])!;
    }

    private IQueryable<TEntity> ApplyResourceFilterInternal<TEntity>(IQueryable query, ResourceContract contract)
        where TEntity : class
    {
        return ApplyResourceFilter((IQueryable<TEntity>)query, contract);
    }

    /// <summary>
    /// Validates field-level write authorization.
    /// </summary>
    /// <param name="contract">The resource contract.</param>
    /// <param name="body">The JSON body being written.</param>
    /// <param name="operation">The CRUD operation.</param>
    /// <exception cref="UnauthorizedAccessException">Thrown when unauthorized fields are present.</exception>
    public void ValidateFieldWriteAuthorization(ResourceContract contract, JsonObject body, CrudOperation operation)
    {
        var authorizer = _sp.GetService<IFieldAuthorizer>();
        if (authorizer is null) return;

        var unauthorized = authorizer.GetUnauthorizedWriteFields(contract, body, operation);
        if (unauthorized.Count > 0)
        {
            throw new UnauthorizedAccessException(
                $"You are not authorized to write the following fields: {string.Join(", ", unauthorized)}");
        }
    }

    /// <summary>
    /// Redacts unauthorized fields from a JSON response.
    /// </summary>
    /// <param name="contract">The resource contract.</param>
    /// <param name="obj">The JSON object to redact.</param>
    public void RedactUnauthorizedFields(ResourceContract contract, JsonObject obj)
    {
        var authorizer = _sp.GetService<IFieldAuthorizer>();
        authorizer?.RedactFields(contract, obj);
    }

    /// <summary>
    /// Redacts unauthorized fields from multiple JSON responses.
    /// </summary>
    /// <param name="contract">The resource contract.</param>
    /// <param name="objects">The JSON objects to redact.</param>
    public void RedactUnauthorizedFields(ResourceContract contract, IEnumerable<JsonObject> objects)
    {
        var authorizer = _sp.GetService<IFieldAuthorizer>();
        if (authorizer is null) return;

        foreach (var obj in objects)
            authorizer.RedactFields(contract, obj);
    }

    /// <summary>
    /// Logs an audit entry for a CRUD operation.
    /// </summary>
    /// <param name="entry">The audit log entry.</param>
    /// <param name="ct">Cancellation token.</param>
    public async Task LogAuditAsync(AuditLogEntry entry, CancellationToken ct = default)
    {
        var logger = _sp.GetService<IAuditLogger>();
        if (logger is not null)
            await logger.LogAsync(entry, ct);
    }

    /// <summary>
    /// Creates an audit log entry for a successful operation.
    /// </summary>
    /// <param name="operation">The CRUD operation.</param>
    /// <param name="resourceKey">The resource key.</param>
    /// <param name="entityId">The entity ID.</param>
    /// <param name="changes">The changes made.</param>
    /// <param name="previousValues">The previous values (for updates).</param>
    /// <returns>An audit log entry.</returns>
    public AuditLogEntry CreateAuditEntry(
        CrudOperation operation,
        string resourceKey,
        string? entityId = null,
        JsonObject? changes = null,
        JsonObject? previousValues = null)
    {
        return new AuditLogEntry
        {
            Operation = operation,
            ResourceKey = resourceKey,
            EntityId = entityId,
            Changes = changes,
            PreviousValues = previousValues,
            Success = true
        };
    }

    /// <summary>
    /// Creates an audit log entry for a failed operation.
    /// </summary>
    /// <param name="operation">The CRUD operation.</param>
    /// <param name="resourceKey">The resource key.</param>
    /// <param name="errorMessage">The error message.</param>
    /// <param name="entityId">The entity ID.</param>
    /// <returns>An audit log entry.</returns>
    public AuditLogEntry CreateFailedAuditEntry(
        CrudOperation operation,
        string resourceKey,
        string errorMessage,
        string? entityId = null)
    {
        return new AuditLogEntry
        {
            Operation = operation,
            ResourceKey = resourceKey,
            EntityId = entityId,
            Success = false,
            ErrorMessage = errorMessage
        };
    }
}
