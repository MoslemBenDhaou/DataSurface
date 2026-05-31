using DataSurface.Core.Contracts;
using DataSurface.EFCore.Interfaces;
using DataSurface.EFCore.Providers;

namespace DataSurface.Dynamic.Contracts;

/// <summary>
/// Combines a static contract provider and a dynamic contract provider into a single view.
/// </summary>
public sealed class CompositeResourceContractProvider : IResourceContractProvider
{
    private readonly IResourceContractProvider _staticProvider;
    private readonly DynamicResourceContractProvider _dynamicProvider;

    /// <summary>
    /// Creates a new composite provider.
    /// </summary>
    /// <param name="staticProvider">Provider for statically defined contracts (concrete type, so this
    /// composite can itself be registered as <see cref="IResourceContractProvider"/> without recursion).</param>
    /// <param name="dynamicProvider">Provider for dynamically defined contracts.</param>
    public CompositeResourceContractProvider(
        StaticResourceContractProvider staticProvider,
        DynamicResourceContractProvider dynamicProvider)
    {
        _staticProvider = staticProvider;
        _dynamicProvider = dynamicProvider;
    }

    /// <inheritdoc />
    public IReadOnlyList<ResourceContract> All
        => _staticProvider.All.Concat(_dynamicProvider.All).ToList();

    /// <inheritdoc />
    public ResourceContract GetByResourceKey(string resourceKey)
    {
        // Prefer static if collision (you can flip)
        var s = _staticProvider.All.FirstOrDefault(x => x.ResourceKey.Equals(resourceKey, StringComparison.OrdinalIgnoreCase));
        if (s != null) return s;

        return _dynamicProvider.GetByResourceKey(resourceKey);
    }
}
