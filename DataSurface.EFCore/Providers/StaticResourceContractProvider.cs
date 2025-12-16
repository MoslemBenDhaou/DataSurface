using DataSurface.Core.Contracts;
using DataSurface.EFCore.Interfaces;

namespace DataSurface.EFCore.Providers;

/// <summary>
/// In-memory implementation of <see cref="IResourceContractProvider"/> backed by a static contract list.
/// </summary>
/// <remarks>
/// Lookups are performed using case-insensitive resource keys.
/// </remarks>
public sealed class StaticResourceContractProvider : IResourceContractProvider
{
    private readonly Dictionary<string, ResourceContract> _byKey;

    /// <summary>
    /// Creates a provider backed by the given <paramref name="contracts"/>.
    /// </summary>
    /// <param name="contracts">Contracts to expose and index by resource key.</param>
    public StaticResourceContractProvider(IReadOnlyList<ResourceContract> contracts)
    {
        All = contracts;
        _byKey = contracts.ToDictionary(c => c.ResourceKey, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Gets all known contracts.
    /// </summary>
    public IReadOnlyList<ResourceContract> All { get; }

    /// <summary>
    /// Gets a contract by its resource key.
    /// </summary>
    /// <param name="resourceKey">The resource key to look up.</param>
    /// <returns>The matching contract.</returns>
    /// <exception cref="KeyNotFoundException">Thrown when no contract exists for the given key.</exception>
    public ResourceContract GetByResourceKey(string resourceKey)
        => _byKey.TryGetValue(resourceKey, out var c)
           ? c
           : throw new KeyNotFoundException($"Unknown resourceKey '{resourceKey}'.");
}
