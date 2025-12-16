using DataSurface.Admin.Services;
using Microsoft.Extensions.DependencyInjection;

namespace DataSurface.Admin.DI;

/// <summary>
/// Dependency injection registration helpers for DataSurface administration services.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers DataSurface administration services.
    /// </summary>
    /// <param name="services">The service collection to register into.</param>
    /// <returns>The same <paramref name="services"/> instance for chaining.</returns>
    public static IServiceCollection AddDataSurfaceAdmin(this IServiceCollection services)
    {
        services.AddScoped<DynamicMetadataAdminService>();
        services.AddScoped<DynamicIndexRebuildService>();
        return services;
    }
}
