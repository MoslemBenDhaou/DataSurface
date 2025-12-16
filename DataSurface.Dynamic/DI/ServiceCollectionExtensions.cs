using DataSurface.Core;
using DataSurface.Dynamic.Contracts;
using DataSurface.Dynamic.Hooks;
using DataSurface.Dynamic.Indexing;
using DataSurface.Dynamic.Services;
using DataSurface.Dynamic.Stores;
using Microsoft.Extensions.DependencyInjection;

namespace DataSurface.Dynamic.DI;

/// <summary>
/// Dependency injection registration helpers for DataSurface dynamic functionality.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers DataSurface dynamic services, including metadata stores, contract providers, indexing and CRUD services.
    /// </summary>
    /// <param name="services">The service collection to register into.</param>
    /// <param name="configure">Optional configuration callback for <see cref="DataSurfaceDynamicOptions"/>.</param>
    /// <returns>The same <paramref name="services"/> instance for chaining.</returns>
    public static IServiceCollection AddDataSurfaceDynamic(this IServiceCollection services, Action<DataSurfaceDynamicOptions>? configure = null)
    {
        var opt = new DataSurfaceDynamicOptions();
        configure?.Invoke(opt);

        services.AddSingleton(opt);

        // contract builder from Phase 1
        services.AddSingleton<DynamicContractBuilder>();

        services.AddScoped<IDynamicEntityDefStore, EfDynamicEntityDefStore>();
        services.AddScoped<IDynamicIndexService, EfDynamicIndexService>();

        services.AddScoped<DynamicResourceContractProvider>();
        services.AddScoped<CrudResourceHookDispatcher>();

        services.AddScoped<DynamicDataSurfaceCrudService>();

        // Composite provider: you can register this as the main IResourceContractProvider
        // only after static provider is registered too.
        services.AddScoped<CompositeResourceContractProvider>();

        return services; 
    }
}
