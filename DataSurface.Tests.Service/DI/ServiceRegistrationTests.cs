using System.Security.Claims;
using DataSurface.EFCore.Interfaces;
using DataSurface.EFCore.Services;
using DataSurface.Http;
using DataSurface.Tests.Service.Shared.Entities;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DataSurface.Tests.Service.DI;

/// <summary>
/// Regression tests for B5: <c>AddDataSurfaceEfCore</c> must register the full CRUD
/// pipeline that the HTTP endpoints resolve from DI, and <c>AddDataSurfaceHttp</c> must
/// bridge the request principal so claim-based tenant resolution works out of the box.
/// </summary>
public class ServiceRegistrationTests
{
    [Fact]
    public void AddDataSurfaceEfCore_And_Http_Resolve_FullPipeline()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<CrudTestDbContext>(o => o.UseInMemoryDatabase(Guid.NewGuid().ToString()));
        services.AddScoped<DbContext>(sp => sp.GetRequiredService<CrudTestDbContext>());

        services.AddDataSurfaceEfCore(_ => { });
        services.AddDataSurfaceHttp();

        using var provider = services.BuildServiceProvider(validateScopes: true);
        using var scope = provider.CreateScope();
        var sp = scope.ServiceProvider;

        // Previously none of these were registered by AddDataSurfaceEfCore, so every HTTP
        // CRUD/bulk/stream endpoint threw at runtime unless the consumer wired them by hand.
        sp.GetService<IDataSurfaceCrudService>().Should().BeOfType<EfDataSurfaceCrudService>();
        sp.GetService<IDataSurfaceBulkService>().Should().NotBeNull();
        sp.GetService<IDataSurfaceStreamingService>().Should().NotBeNull();

        // Registering the security dispatcher is what makes tenant isolation / row / field
        // authorization active out of the box.
        sp.GetService<CrudSecurityDispatcher>().Should().NotBeNull();

        // AddDataSurfaceHttp bridges HttpContext.User into DI (ASP.NET Core does not).
        sp.GetService<ClaimsPrincipal>().Should().NotBeNull();
    }
}
