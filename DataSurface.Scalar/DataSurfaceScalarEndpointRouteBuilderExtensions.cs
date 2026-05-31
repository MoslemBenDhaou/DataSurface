using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Scalar.AspNetCore;

namespace DataSurface.Scalar;

/// <summary>
/// Endpoint-mapping helpers for serving the Scalar API reference UI for DataSurface, alongside the
/// existing Swagger/Swashbuckle integration (see <c>DataSurface.OpenApi</c>).
/// </summary>
public static class DataSurfaceScalarEndpointRouteBuilderExtensions
{
    /// <summary>
    /// Maps the Scalar API reference UI, pointed at the Swashbuckle-generated OpenAPI document.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This mirrors how Swagger is wired: it is additive and does not change any Swagger behavior, so
    /// the Swagger UI and the Scalar UI can be served side by side. Scalar requires no service
    /// registration of its own — only that an OpenAPI document is exposed (for example via
    /// <c>AddEndpointsApiExplorer()</c> + <c>AddSwaggerGen(...)</c> + <c>UseSwagger()</c>, optionally
    /// with <c>AddDataSurfaceOpenApi</c> for the typed DataSurface schemas).
    /// </para>
    /// <example>
    /// <code>
    /// builder.Services.AddEndpointsApiExplorer();
    /// builder.Services.AddSwaggerGen(o => builder.Services.AddDataSurfaceOpenApi(o));
    /// // ...
    /// app.MapDataSurfaceCrud();
    /// app.UseSwagger();        // serves /swagger/{documentName}/swagger.json
    /// app.UseSwaggerUI();      // Swagger UI (optional, can coexist)
    /// app.MapDataSurfaceScalar(); // Scalar UI at /scalar
    /// </code>
    /// </example>
    /// </remarks>
    /// <param name="endpoints">The endpoint route builder.</param>
    /// <param name="endpointPrefix">The route the Scalar UI is served at. Defaults to <c>/scalar</c>.</param>
    /// <param name="title">The reference UI title.</param>
    /// <param name="openApiRoutePattern">The route pattern of the OpenAPI JSON document. Defaults to the
    /// Swashbuckle convention <c>/swagger/{documentName}/swagger.json</c>. For the built-in
    /// <c>Microsoft.AspNetCore.OpenApi</c> instead, use <c>/openapi/{documentName}.json</c>.</param>
    /// <param name="configure">Optional callback to further customize <see cref="ScalarOptions"/>
    /// (theme, layout, servers, auth schemes, etc.).</param>
    /// <returns>An <see cref="IEndpointConventionBuilder"/> for further endpoint configuration.</returns>
    public static IEndpointConventionBuilder MapDataSurfaceScalar(
        this IEndpointRouteBuilder endpoints,
        string endpointPrefix = "/scalar",
        string title = "DataSurface API",
        string openApiRoutePattern = "/swagger/{documentName}/swagger.json",
        Action<ScalarOptions>? configure = null)
    {
        return endpoints.MapScalarApiReference(endpointPrefix, options =>
        {
            options.WithTitle(title);
            options.WithOpenApiRoutePattern(openApiRoutePattern);
            configure?.Invoke(options);
        });
    }
}
