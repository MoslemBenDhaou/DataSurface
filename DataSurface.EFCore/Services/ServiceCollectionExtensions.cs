using DataSurface.Core;
using DataSurface.EFCore.Context;
using DataSurface.EFCore.Interfaces;
using DataSurface.EFCore.Mapper;
using DataSurface.EFCore.Options;
using DataSurface.EFCore.Providers;
using DataSurface.EFCore.Queries;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace DataSurface.EFCore.Services;

/// <summary>
/// Dependency injection registration helpers for DataSurface's Entity Framework Core integration.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers DataSurface EF Core services into the provided <paramref name="services"/> collection.
    /// </summary>
    /// <param name="services">The service collection to register into.</param>
    /// <param name="configure">Callback used to configure <see cref="DataSurfaceEfCoreOptions"/>.</param>
    /// <returns>The same <paramref name="services"/> instance for chaining.</returns>
    /// <remarks>
    /// This method:
    /// - registers <see cref="DataSurfaceEfCoreOptions"/> as a singleton
    /// - registers a Core <see cref="ContractBuilder"/> using <see cref="DataSurfaceEfCoreOptions.ContractBuilderOptions"/>
    /// - builds a static contract set from <see cref="DataSurfaceEfCoreOptions.AssembliesToScan"/>
    /// - registers <see cref="IResourceContractProvider"/> backed by <see cref="StaticResourceContractProvider"/>
    /// - registers <see cref="EfCrudQueryEngine"/> and <see cref="EfCrudMapper"/> as scoped services
    /// </remarks>
    public static IServiceCollection AddDataSurfaceEfCore(
        this IServiceCollection services,
        Action<DataSurfaceEfCoreOptions> configure)
    {
        var opt = new DataSurfaceEfCoreOptions();
        configure(opt);

        services.AddSingleton(opt);
        // Feature flags are an AND-gate: a feature runs only when its flag is enabled AND its wiring is
        // present. Registered so the dispatchers/mapper/service can enforce the flags. Defaults to all-on.
        services.AddSingleton(opt.Features);

        // Propagate UseCamelCaseApiNames from EfCore options to the ContractBuilder options
        opt.ContractBuilderOptions.UseCamelCaseApiNames = opt.UseCamelCaseApiNames;

        services.AddSingleton(new ContractBuilder(opt.ContractBuilderOptions));

        // Register the static provider as its concrete type and alias the interface to it, so a
        // composite provider (DataSurface.Dynamic) can inject the concrete static provider and itself
        // be registered as IResourceContractProvider without a circular registration.
        services.AddSingleton(sp =>
        {
            var builder = sp.GetRequiredService<ContractBuilder>();
            var contracts = opt.AssembliesToScan
                .SelectMany(a => builder.BuildFromAssembly(a))
                .ToList();

            return new StaticResourceContractProvider(contracts);
        });
        services.TryAddSingleton<IResourceContractProvider>(sp => sp.GetRequiredService<StaticResourceContractProvider>());

        services.AddScoped<EfCrudQueryEngine>();
        services.AddScoped<EfCrudMapper>();

        // Process-wide cache of compiled EF queries (used by the by-id read fast path).
        services.TryAddSingleton<CompiledQueryCache>();

        // Core CRUD pipeline required by the HTTP layer. Registered with TryAdd so a
        // consumer (or the dynamic CRUD router) can override any of these. Registering
        // CrudSecurityDispatcher here is what makes tenant isolation, row/field
        // authorization and audit logging active out of the box (each is a no-op until
        // the corresponding IResourceFilter / IResourceAuthorizer / IFieldAuthorizer /
        // IAuditLogger / ITenantResolver is registered).
        services.TryAddScoped<CrudHookDispatcher>();
        services.TryAddSingleton<CrudOverrideRegistry>();
        services.TryAddScoped<CrudSecurityDispatcher>();
        // Register the concrete EF service and alias the interface to it, so a backend router
        // (DataSurface.Dynamic) can inject the concrete EF service without a circular IDataSurfaceCrudService.
        services.TryAddScoped<EfDataSurfaceCrudService>();
        services.TryAddScoped<IDataSurfaceCrudService>(sp => sp.GetRequiredService<EfDataSurfaceCrudService>());
        services.TryAddScoped<IDataSurfaceBulkService, EfDataSurfaceBulkService>();
        services.TryAddScoped<IDataSurfaceStreamingService, EfDataSurfaceStreamingService>();

        return services;
    }

    /// <summary>
    /// Registers DataSurface EF Core services and aliases the application's <typeparamref name="TDbContext"/>
    /// to the base <see cref="DbContext"/> that the CRUD services depend on. Use this overload so callers do
    /// not have to add the <c>AddScoped&lt;DbContext&gt;(...)</c> alias themselves.
    /// </summary>
    /// <typeparam name="TDbContext">The application's <see cref="DbContext"/> type (registered via AddDbContext).</typeparam>
    /// <param name="services">The service collection to register into.</param>
    /// <param name="configure">Callback used to configure <see cref="DataSurfaceEfCoreOptions"/>.</param>
    /// <returns>The same <paramref name="services"/> instance for chaining.</returns>
    public static IServiceCollection AddDataSurfaceEfCore<TDbContext>(
        this IServiceCollection services,
        Action<DataSurfaceEfCoreOptions> configure)
        where TDbContext : DbContext
    {
        services.TryAddScoped<DbContext>(sp => sp.GetRequiredService<TDbContext>());
        return services.AddDataSurfaceEfCore(configure);
    }
}
