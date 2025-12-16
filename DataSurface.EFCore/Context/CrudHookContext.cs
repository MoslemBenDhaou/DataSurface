using DataSurface.Core.Contracts;
using DataSurface.Core.Enums;
using Microsoft.EntityFrameworkCore;

namespace DataSurface.EFCore.Context;

/// <summary>
/// Context passed to CRUD hooks describing the current operation, contract, and services.
/// </summary>
public sealed class CrudHookContext
{
    /// <summary>
    /// Gets the CRUD operation being executed.
    /// </summary>
    public required CrudOperation Operation { get; init; }

    /// <summary>
    /// Gets the resource contract associated with the operation.
    /// </summary>
    public required ResourceContract Contract { get; init; }

    /// <summary>
    /// Gets the EF Core database context.
    /// </summary>
    public required DbContext Db { get; init; }

    /// <summary>
    /// Gets the service provider for resolving scoped services.
    /// </summary>
    public required IServiceProvider Services { get; init; }

    // optional: can be used by callers (HTTP layer can set HttpContext)
    /// <summary>
    /// Gets a bag for passing additional data between layers and hooks.
    /// </summary>
    public IDictionary<string, object?> Items { get; } = new Dictionary<string, object?>();
}
