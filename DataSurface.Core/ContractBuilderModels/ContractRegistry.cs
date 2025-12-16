using System.Collections.Concurrent;
using System.Reflection;
using DataSurface.Core.Annotations;
using DataSurface.Core.Contracts;

namespace DataSurface.Core.ContractBuilderModels;

/// <summary>
/// Thread-safe cache for <see cref="ResourceContract"/> definitions built from CLR types.
/// </summary>
/// <remarks>
/// This registry memoizes contract generation per <see cref="Assembly"/> so multiple callers can reuse
/// the same normalized resource contract metadata.
/// </remarks>
public sealed class ContractRegistry
{
    private readonly ConcurrentDictionary<Assembly, IReadOnlyList<ResourceContract>> _cache = new();
    private readonly ContractBuilder _builder;

    /// <summary>
    /// Creates a new registry.
    /// </summary>
    /// <param name="builder">
    /// Optional builder used to produce contracts. If <see langword="null"/>, a default <see cref="ContractBuilder"/>
    /// instance is created.
    /// </param>
    public ContractRegistry(ContractBuilder? builder = null)
        => _builder = builder ?? new ContractBuilder();

    /// <summary>
    /// Builds (or returns a cached) set of resource contracts from the given <paramref name="a"/>.
    /// </summary>
    /// <param name="a">Assembly to scan for resource types annotated with <see cref="CrudResourceAttribute"/>.</param>
    /// <returns>The normalized resource contracts produced from the assembly.</returns>
    public IReadOnlyList<ResourceContract> FromAssembly(Assembly a)
        => _cache.GetOrAdd(a, (Func<Assembly, IReadOnlyList<ResourceContract>>) _builder.BuildFromAssembly);
}
