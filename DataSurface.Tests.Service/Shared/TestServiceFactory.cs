using DataSurface.Core.Contracts;
using DataSurface.EFCore;
using DataSurface.EFCore.Caching;
using DataSurface.EFCore.Context;
using DataSurface.EFCore.Interfaces;
using DataSurface.EFCore.Mapper;
using DataSurface.EFCore.Observability;
using DataSurface.EFCore.Providers;
using DataSurface.EFCore.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace DataSurface.Tests.Service.Shared;

/// <summary>
/// Factory for creating fully wired <see cref="EfDataSurfaceCrudService"/> instances
/// backed by an in-memory database for behavioral testing.
/// </summary>
public sealed class TestServiceFactory : IDisposable
{
    private readonly ServiceProvider _sp;
    private readonly DbContext _db;

    public DbContext Db => _db;
    public IServiceProvider Services => _sp;
    public EfDataSurfaceCrudService CrudService { get; }
    public IResourceContractProvider Contracts { get; }

    public TestServiceFactory(DbContext db, IReadOnlyList<ResourceContract> contracts, Action<IServiceCollection>? configure = null)
    {
        _db = db;

        var services = new ServiceCollection();
        services.AddSingleton(db);
        services.AddSingleton<DbContext>(db);
        services.AddSingleton<IResourceContractProvider>(new StaticResourceContractProvider(contracts));
        services.AddSingleton<EfCrudQueryEngine>();
        services.AddSingleton<EfCrudMapper>();
        services.AddSingleton<CrudHookDispatcher>();
        services.AddSingleton<CrudOverrideRegistry>();
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));

        configure?.Invoke(services);

        _sp = services.BuildServiceProvider();

        Contracts = _sp.GetRequiredService<IResourceContractProvider>();

        CrudService = new EfDataSurfaceCrudService(
            db: _sp.GetRequiredService<DbContext>(),
            contracts: Contracts,
            query: _sp.GetRequiredService<EfCrudQueryEngine>(),
            mapper: _sp.GetRequiredService<EfCrudMapper>(),
            sp: _sp,
            hooks: _sp.GetRequiredService<CrudHookDispatcher>(),
            overrides: _sp.GetRequiredService<CrudOverrideRegistry>(),
            logger: _sp.GetRequiredService<ILogger<EfDataSurfaceCrudService>>(),
            security: _sp.GetService<CrudSecurityDispatcher>(),
            metrics: _sp.GetService<DataSurfaceMetrics>(),
            cache: _sp.GetService<IQueryResultCache>());
    }

    public void Dispose()
    {
        _db.Dispose();
        _sp.Dispose();
    }
}
