namespace DataSurface.EFCore.Interfaces;

/// <summary>
/// Interface for resolving the current tenant ID.
/// </summary>
/// <remarks>
/// Implement this interface to provide custom tenant resolution logic.
/// Register it in DI to override the default claim-based resolution.
/// </remarks>
public interface ITenantResolver
{
    /// <summary>
    /// Gets the current tenant ID.
    /// </summary>
    /// <returns>The tenant ID, or null if not available.</returns>
    string? GetTenantId();
}
