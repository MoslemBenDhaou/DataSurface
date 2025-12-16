using DataSurface.Core;
using DataSurface.EFCore.Interfaces;
using DataSurface.EFCore.Mapper;
using DataSurface.EFCore.Options;
using DataSurface.EFCore.Providers;
using Microsoft.Extensions.DependencyInjection;

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
        services.AddSingleton(new ContractBuilder(opt.ContractBuilderOptions));

        services.AddSingleton<IResourceContractProvider>(sp =>
        {
            var builder = sp.GetRequiredService<ContractBuilder>();
            var contracts = opt.AssembliesToScan
                .SelectMany(a => builder.BuildFromAssembly(a))
                .ToList();

            return new StaticResourceContractProvider(contracts);
        });

        services.AddScoped<EfCrudQueryEngine>();
        services.AddScoped<EfCrudMapper>();

        return services;
    }
}
