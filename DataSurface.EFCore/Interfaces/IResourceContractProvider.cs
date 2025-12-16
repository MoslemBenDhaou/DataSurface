using DataSurface.Core.Contracts;

namespace DataSurface.EFCore.Interfaces;

/// <summary>
/// Provides access to the set of <see cref="ResourceContract"/> definitions used by the EF Core integration.
/// </summary>
public interface IResourceContractProvider
{
    /// <summary>
    /// Gets all known resource contracts.
    /// </summary>
    IReadOnlyList<ResourceContract> All { get; }

    /// <summary>
    /// Gets a resource contract by its resource key.
    /// </summary>
    /// <param name="resourceKey">The resource key to look up.</param>
    /// <returns>The matching resource contract.</returns>
    ResourceContract GetByResourceKey(string resourceKey);
}
