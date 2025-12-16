using DataSurface.Core.Enums;

namespace DataSurface.Core.Annotations;

/// <summary>
/// Declares an authorization policy for a resource or operation.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true)]
public sealed class CrudAuthorizeAttribute : Attribute
{
    /// <summary>
    /// Creates a new authorization policy marker.
    /// </summary>
    /// <param name="policy">The policy name.</param>
    public CrudAuthorizeAttribute(string policy) => Policy = policy;

    /// <summary>
    /// Gets the policy name.
    /// </summary>
    public string Policy { get; }
    /// <summary>
    /// Gets or sets the operation this policy applies to.
    /// </summary>
    /// <remarks>
    /// When <see langword="null"/>, the policy applies to all operations.
    /// </remarks>
    public CrudOperation? Operation { get; set; }   // if null => applies to all ops
}