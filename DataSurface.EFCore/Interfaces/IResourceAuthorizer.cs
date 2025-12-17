using DataSurface.Core.Contracts;
using DataSurface.Core.Enums;

namespace DataSurface.EFCore.Interfaces;

/// <summary>
/// Provides resource-level authorization checks for individual entities.
/// </summary>
/// <remarks>
/// <para>
/// This interface enables fine-grained authorization at the resource instance level,
/// answering questions like "Can this user update Order #123?" rather than just
/// "Can this user access the Orders endpoint?"
/// </para>
/// <para>
/// Implementations typically integrate with ASP.NET Core's <c>IAuthorizationService</c>
/// to leverage policy-based authorization with resource requirements.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// public class PolicyResourceAuthorizer : IResourceAuthorizer
/// {
///     private readonly IAuthorizationService _auth;
///     private readonly IHttpContextAccessor _http;
///     
///     public PolicyResourceAuthorizer(IAuthorizationService auth, IHttpContextAccessor http)
///     {
///         _auth = auth;
///         _http = http;
///     }
///     
///     public async Task&lt;AuthorizationResult&gt; AuthorizeAsync(
///         ResourceContract contract,
///         object? entity,
///         CrudOperation operation,
///         CancellationToken ct)
///     {
///         var user = _http.HttpContext?.User;
///         if (user is null)
///             return AuthorizationResult.Fail("No authenticated user.");
///         
///         // Map CRUD operation to policy name
///         var policyName = $"{contract.ResourceKey}.{operation}";
///         
///         // Use ASP.NET Core authorization with resource
///         var result = await _auth.AuthorizeAsync(user, entity, policyName);
///         
///         return result.Succeeded 
///             ? AuthorizationResult.Success() 
///             : AuthorizationResult.Fail("Access denied.");
///     }
/// }
/// </code>
/// </example>
public interface IResourceAuthorizer
{
    /// <summary>
    /// Determines whether the current user is authorized to perform an operation on a resource.
    /// </summary>
    /// <param name="contract">The resource contract.</param>
    /// <param name="entity">The entity instance being accessed (null for Create/List operations).</param>
    /// <param name="operation">The CRUD operation being performed.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>An authorization result indicating success or failure with reason.</returns>
    Task<AuthorizationResult> AuthorizeAsync(
        ResourceContract contract,
        object? entity,
        CrudOperation operation,
        CancellationToken ct = default);
}

/// <summary>
/// Typed resource authorizer for compile-time type safety.
/// </summary>
/// <typeparam name="TEntity">The entity type.</typeparam>
/// <example>
/// <code>
/// public class OrderAuthorizer : IResourceAuthorizer&lt;Order&gt;
/// {
///     private readonly IHttpContextAccessor _http;
///     
///     public OrderAuthorizer(IHttpContextAccessor http) => _http = http;
///     
///     public Task&lt;AuthorizationResult&gt; AuthorizeAsync(
///         ResourceContract contract,
///         Order? entity,
///         CrudOperation operation,
///         CancellationToken ct)
///     {
///         var userId = _http.HttpContext?.User.FindFirst("sub")?.Value;
///         
///         // Owner can do anything with their orders
///         if (entity?.OwnerId == userId)
///             return Task.FromResult(AuthorizationResult.Success());
///         
///         // Admins can access all orders
///         if (_http.HttpContext?.User.IsInRole("Admin") == true)
///             return Task.FromResult(AuthorizationResult.Success());
///         
///         return Task.FromResult(AuthorizationResult.Fail("You can only access your own orders."));
///     }
/// }
/// </code>
/// </example>
public interface IResourceAuthorizer<TEntity> where TEntity : class
{
    /// <summary>
    /// Determines whether the current user is authorized to perform an operation on the entity.
    /// </summary>
    /// <param name="contract">The resource contract.</param>
    /// <param name="entity">The entity instance (null for Create/List operations).</param>
    /// <param name="operation">The CRUD operation being performed.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>An authorization result indicating success or failure with reason.</returns>
    Task<AuthorizationResult> AuthorizeAsync(
        ResourceContract contract,
        TEntity? entity,
        CrudOperation operation,
        CancellationToken ct = default);
}

/// <summary>
/// Represents the result of a resource authorization check.
/// </summary>
public sealed class AuthorizationResult
{
    /// <summary>
    /// Gets whether the authorization succeeded.
    /// </summary>
    public bool Succeeded { get; }

    /// <summary>
    /// Gets the failure reason if authorization failed.
    /// </summary>
    public string? FailureReason { get; }

    private AuthorizationResult(bool succeeded, string? failureReason = null)
    {
        Succeeded = succeeded;
        FailureReason = failureReason;
    }

    /// <summary>
    /// Creates a successful authorization result.
    /// </summary>
    public static AuthorizationResult Success() => new(true);

    /// <summary>
    /// Creates a failed authorization result with a reason.
    /// </summary>
    /// <param name="reason">The reason for failure.</param>
    public static AuthorizationResult Fail(string reason) => new(false, reason);

    /// <summary>
    /// Creates an authorization result from a boolean.
    /// </summary>
    /// <param name="succeeded">Whether authorization succeeded.</param>
    /// <param name="failureReason">Optional failure reason if not succeeded.</param>
    public static AuthorizationResult FromBool(bool succeeded, string? failureReason = null)
        => succeeded ? Success() : Fail(failureReason ?? "Access denied.");
}
