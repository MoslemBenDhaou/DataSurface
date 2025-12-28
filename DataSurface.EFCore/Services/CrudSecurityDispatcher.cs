using System.Linq.Expressions;
using System.Security.Claims;
using System.Text.Json.Nodes;
using DataSurface.Core.Contracts;
using DataSurface.Core.Enums;
using DataSurface.EFCore.Interfaces;
using Microsoft.Extensions.DependencyInjection;

namespace DataSurface.EFCore.Services;

/// <summary>
/// Dispatches security-related operations including row-level filtering, field authorization, resource authorization, and audit logging.
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
    /// Applies tenant isolation filter to a query based on the contract's tenant configuration.
    /// </summary>
    /// <typeparam name="TEntity">The entity type.</typeparam>
    /// <param name="query">The query to filter.</param>
    /// <param name="contract">The resource contract.</param>
    /// <returns>The filtered query.</returns>
    /// <exception cref="UnauthorizedAccessException">Thrown when tenant claim is required but missing.</exception>
    public IQueryable<TEntity> ApplyTenantFilter<TEntity>(IQueryable<TEntity> query, ResourceContract contract)
        where TEntity : class
    {
        if (contract.Tenant is null) return query;

        var tenantValue = GetTenantValue(contract.Tenant);
        if (tenantValue is null)
        {
            if (contract.Tenant.Required)
                throw new UnauthorizedAccessException($"Tenant claim '{contract.Tenant.ClaimType}' is required but was not found.");
            return query;
        }

        // Build filter expression: e => e.TenantField == tenantValue
        var param = Expression.Parameter(typeof(TEntity), "e");
        var prop = Expression.Property(param, contract.Tenant.FieldName);
        var constant = Expression.Constant(tenantValue, prop.Type);
        var eq = Expression.Equal(prop, constant);
        var lambda = Expression.Lambda<Func<TEntity, bool>>(eq, param);

        return query.Where(lambda);
    }

    /// <summary>
    /// Applies tenant isolation filter to a non-generic query.
    /// </summary>
    public IQueryable ApplyTenantFilter(IQueryable query, Type entityType, ResourceContract contract)
    {
        if (contract.Tenant is null) return query;

        var method = typeof(CrudSecurityDispatcher)
            .GetMethod(nameof(ApplyTenantFilterInternal),
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
            .MakeGenericMethod(entityType);

        return (IQueryable)method.Invoke(this, [query, contract])!;
    }

    private IQueryable<TEntity> ApplyTenantFilterInternal<TEntity>(IQueryable query, ResourceContract contract)
        where TEntity : class
    {
        return ApplyTenantFilter((IQueryable<TEntity>)query, contract);
    }

    /// <summary>
    /// Sets the tenant value on an entity during create operations.
    /// </summary>
    /// <param name="entity">The entity to set the tenant on.</param>
    /// <param name="contract">The resource contract.</param>
    /// <exception cref="UnauthorizedAccessException">Thrown when tenant claim is required but missing.</exception>
    public void SetTenantValue(object entity, ResourceContract contract)
    {
        if (contract.Tenant is null) return;

        var tenantValue = GetTenantValue(contract.Tenant);
        if (tenantValue is null)
        {
            if (contract.Tenant.Required)
                throw new UnauthorizedAccessException($"Tenant claim '{contract.Tenant.ClaimType}' is required but was not found.");
            return;
        }

        var prop = entity.GetType().GetProperty(contract.Tenant.FieldName);
        if (prop is not null && prop.CanWrite)
        {
            var convertedValue = Convert.ChangeType(tenantValue, prop.PropertyType);
            prop.SetValue(entity, convertedValue);
        }
    }

    private string? GetTenantValue(TenantContract tenant)
    {
        // Try to get tenant resolver from DI
        var resolver = _sp.GetService<ITenantResolver>();
        if (resolver is not null)
            return resolver.GetTenantId();

        // Fallback: try to get ClaimsPrincipal from DI (registered by ASP.NET Core)
        var principal = _sp.GetService<System.Security.Claims.ClaimsPrincipal>();
        return principal?.FindFirst(tenant.ClaimType)?.Value;
    }

    /// <summary>
    /// Authorizes access to a specific resource instance.
    /// </summary>
    /// <typeparam name="TEntity">The entity type.</typeparam>
    /// <param name="contract">The resource contract.</param>
    /// <param name="entity">The entity instance (null for Create/List).</param>
    /// <param name="operation">The CRUD operation.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <exception cref="UnauthorizedAccessException">Thrown when authorization fails.</exception>
    public async Task AuthorizeResourceAsync<TEntity>(
        ResourceContract contract,
        TEntity? entity,
        CrudOperation operation,
        CancellationToken ct = default) where TEntity : class
    {
        // Try typed authorizer first
        var typedAuth = _sp.GetService<IResourceAuthorizer<TEntity>>();
        if (typedAuth is not null)
        {
            var result = await typedAuth.AuthorizeAsync(contract, entity, operation, ct);
            if (!result.Succeeded)
                throw new UnauthorizedAccessException(result.FailureReason ?? "Access denied.");
            return;
        }

        // Then try non-generic authorizer
        var globalAuth = _sp.GetService<IResourceAuthorizer>();
        if (globalAuth is not null)
        {
            var result = await globalAuth.AuthorizeAsync(contract, entity, operation, ct);
            if (!result.Succeeded)
                throw new UnauthorizedAccessException(result.FailureReason ?? "Access denied.");
        }
    }

    /// <summary>
    /// Authorizes access to a specific resource instance (non-generic).
    /// </summary>
    /// <param name="contract">The resource contract.</param>
    /// <param name="entity">The entity instance (null for Create/List).</param>
    /// <param name="entityType">The entity type.</param>
    /// <param name="operation">The CRUD operation.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <exception cref="UnauthorizedAccessException">Thrown when authorization fails.</exception>
    public async Task AuthorizeResourceAsync(
        ResourceContract contract,
        object? entity,
        Type entityType,
        CrudOperation operation,
        CancellationToken ct = default)
    {
        // Use reflection to call the generic version
        var method = typeof(CrudSecurityDispatcher)
            .GetMethod(nameof(AuthorizeResourceAsync), 1, [typeof(ResourceContract), Type.MakeGenericMethodParameter(0), typeof(CrudOperation), typeof(CancellationToken)])!
            .MakeGenericMethod(entityType);

        await (Task)method.Invoke(this, [contract, entity, operation, ct])!;
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
