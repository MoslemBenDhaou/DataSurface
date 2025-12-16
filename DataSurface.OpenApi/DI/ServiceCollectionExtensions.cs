using Microsoft.Extensions.DependencyInjection;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace DataSurface.OpenApi.DI;

/// <summary>
/// Dependency injection registration helpers for DataSurface OpenAPI/Swagger integration.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers DataSurface OpenAPI customizations into the provided Swagger generator.
    /// </summary>
    /// <param name="services">The service collection to register into.</param>
    /// <param name="swagger">The Swagger generator options to configure.</param>
    /// <returns>The same <paramref name="services"/> instance for chaining.</returns>
    public static IServiceCollection AddDataSurfaceOpenApi(this IServiceCollection services, SwaggerGenOptions swagger)
    {
        // DI-enabled filter
        swagger.OperationFilter<DataSurfaceCrudOperationFilter>();
        return services;
    }
}
