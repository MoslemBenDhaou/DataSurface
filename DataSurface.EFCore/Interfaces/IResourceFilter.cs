using System.Linq.Expressions;
using DataSurface.Core.Contracts;

namespace DataSurface.EFCore.Interfaces;

/// <summary>
/// Provides row-level security by filtering queries based on user context.
/// </summary>
/// <remarks>
/// Implement this interface to restrict which records a user can access.
/// The filter is applied to all List and Get operations for the resource.
/// </remarks>
/// <example>
/// <code>
/// public class TenantResourceFilter : IResourceFilter&lt;Order&gt;
/// {
///     private readonly ITenantContext _tenant;
///     
///     public TenantResourceFilter(ITenantContext tenant) => _tenant = tenant;
///     
///     public Expression&lt;Func&lt;Order, bool&gt;&gt; GetFilter(ResourceContract contract)
///         => o => o.TenantId == _tenant.TenantId;
/// }
/// </code>
/// </example>
public interface IResourceFilter<TEntity> where TEntity : class
{
    /// <summary>
    /// Gets a filter expression to apply to queries for this entity type.
    /// </summary>
    /// <param name="contract">The resource contract.</param>
    /// <returns>A filter expression, or <see langword="null"/> to apply no filter.</returns>
    Expression<Func<TEntity, bool>>? GetFilter(ResourceContract contract);
}

/// <summary>
/// Provides row-level security by filtering queries based on user context (non-generic).
/// </summary>
/// <remarks>
/// Use this interface when you need to apply filters dynamically without knowing the entity type at compile time.
/// </remarks>
public interface IResourceFilter
{
    /// <summary>
    /// Gets the entity types this filter applies to.
    /// </summary>
    IEnumerable<Type> AppliesTo { get; }

    /// <summary>
    /// Gets a filter expression for the specified entity type.
    /// </summary>
    /// <param name="entityType">The entity type.</param>
    /// <param name="contract">The resource contract.</param>
    /// <returns>A filter expression as <see cref="LambdaExpression"/>, or <see langword="null"/>.</returns>
    LambdaExpression? GetFilter(Type entityType, ResourceContract contract);
}
