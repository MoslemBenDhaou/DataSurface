using System.Collections.Concurrent;
using DataSurface.Core.Contracts;

namespace DataSurface.Dynamic.Contracts;

/// <summary>
/// Application-wide cache of built dynamic resource contracts, keyed by entity key and stamped
/// with the definition row's <c>UpdatedAt</c> for freshness checks.
/// </summary>
/// <remarks>
/// Registered as a singleton and shared by every (scoped) <see cref="DynamicResourceContractProvider"/>
/// instance, so contracts loaded at warm-up or by any request are visible to discovery/schema
/// consumers via <see cref="DynamicResourceContractProvider.All"/>. A DI singleton (rather than a
/// process-wide static) keeps independent service providers — e.g. parallel test hosts pointing
/// at different databases — from polluting each other's cache.
/// </remarks>
public sealed class DynamicContractCache
{
    /// <summary>
    /// Gets the cached contracts keyed by entity key, each stamped with the definition row's
    /// <c>UpdatedAt</c> timestamp at build time.
    /// </summary>
    public ConcurrentDictionary<string, (ResourceContract Contract, DateTime UpdatedAt)> Entries { get; } =
        new(StringComparer.OrdinalIgnoreCase);
}
