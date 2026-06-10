using DataSurface.Core;
using DataSurface.Dynamic.Contracts;
using DataSurface.Dynamic.Hooks;
using DataSurface.Dynamic.Indexing;
using DataSurface.Dynamic.Services;
using DataSurface.Dynamic.Stores;
using DataSurface.EFCore.Interfaces;
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

        // Shared contract cache: the provider is scoped, so the cache must outlive it for
        // `All` (discovery/schema) to see contracts loaded by other scopes / the warm-up.
        services.AddSingleton<DynamicContractCache>();
        services.AddScoped<DynamicResourceContractProvider>();

        if (opt.WarmUpContractsOnStart)
            services.AddHostedService<DynamicContractWarmupHostedService>();
        services.AddScoped<CrudResourceHookDispatcher>();

        services.AddScoped<DynamicDataSurfaceCrudService>();

        // Composite provider, registered as THE app-wide IResourceContractProvider so that every
        // consumer (bulk/streaming validation, dynamic expand targets, the router) resolves both
        // static and dynamic resource keys. Overrides the static-only alias from AddDataSurfaceEfCore.
        services.AddScoped<CompositeResourceContractProvider>();
        services.AddScoped<IResourceContractProvider>(sp => sp.GetRequiredService<CompositeResourceContractProvider>());

        // Route IDataSurfaceCrudService by backend (EF vs dynamic). This overrides the EF-only
        // alias registered by AddDataSurfaceEfCore so that dynamic resources reached via the HTTP
        // catch-all (and bulk/streaming) execute on the dynamic service — applying tenant/security.
        services.AddScoped<DataSurfaceCrudRouter>();
        services.AddScoped<IDataSurfaceCrudService>(sp => sp.GetRequiredService<DataSurfaceCrudRouter>());

        return services;
    }
}
