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
    private CrudOperation _operation;

    /// <summary>
    /// Gets or sets the operation this policy applies to.
    /// </summary>
    /// <remarks>
    /// When not set, the policy applies to all operations. (A nullable enum is not a valid
    /// attribute parameter type, so "unset" is tracked via <see cref="HasOperation"/>.)
    /// </remarks>
    public CrudOperation Operation
    {
        get => _operation;
        set
        {
            _operation = value;
            HasOperation = true;
        }
    }

    /// <summary>
    /// Gets whether <see cref="Operation"/> was explicitly set.
    /// </summary>
    public bool HasOperation { get; private set; }
}