using DataSurface.Core.Contracts;
using DataSurface.EFCore.Interfaces;

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
    /// <param name="staticProvider">Provider for statically defined contracts.</param>
    /// <param name="dynamicProvider">Provider for dynamically defined contracts.</param>
    public CompositeResourceContractProvider(
        IResourceContractProvider staticProvider,
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
