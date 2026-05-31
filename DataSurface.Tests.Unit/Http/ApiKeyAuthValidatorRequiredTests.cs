using DataSurface.Http;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DataSurface.Tests.Unit.Http;

/// <summary>
/// API key authentication must fail closed: enabling it without registering an <see cref="IApiKeyValidator"/>
/// would otherwise accept any non-empty key (presence-only). <c>MapDataSurfaceCrud</c> now rejects that
/// misconfiguration at startup.
/// </summary>
public class ApiKeyAuthValidatorRequiredTests
{
    // Minimal IEndpointRouteBuilder so MapDataSurfaceCrud's startup checks can run without a full host.
    private sealed class TestEndpointRouteBuilder(IServiceProvider sp) : IEndpointRouteBuilder
    {
        public IServiceProvider ServiceProvider { get; } = sp;
        public ICollection<EndpointDataSource> DataSources { get; } = new List<EndpointDataSource>();
        public IApplicationBuilder CreateApplicationBuilder() => new ApplicationBuilder(ServiceProvider);
    }

    private sealed class AcceptAllValidator : IApiKeyValidator
    {
        public Task<bool> ValidateAsync(string apiKey, CancellationToken ct = default) => Task.FromResult(true);
    }

    [Fact]
    public void MapDataSurfaceCrud_ApiKeyAuthEnabled_NoValidator_ThrowsAtStartup()
    {
        var app = new TestEndpointRouteBuilder(new ServiceCollection().BuildServiceProvider());

        var act = () => app.MapDataSurfaceCrud(new DataSurfaceHttpOptions { EnableApiKeyAuth = true });

        act.Should().Throw<InvalidOperationException>().WithMessage("*IApiKeyValidator*");
    }

    [Fact]
    public void MapDataSurfaceCrud_ApiKeyAuthEnabled_WithValidator_DoesNotThrowForValidatorCheck()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IApiKeyValidator, AcceptAllValidator>();
        var app = new TestEndpointRouteBuilder(services.BuildServiceProvider());

        var act = () => app.MapDataSurfaceCrud(new DataSurfaceHttpOptions { EnableApiKeyAuth = true, MapStaticResources = false });

        act.Should().NotThrow();
    }
}
