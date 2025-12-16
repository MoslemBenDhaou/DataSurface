using DataSurface.Admin.Dtos;
using DataSurface.Admin.Services;
using DataSurface.Http;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace DataSurface.Admin;

/// <summary>
/// Extension methods for mapping DataSurface administration endpoints.
/// </summary>
public static class DataSurfaceAdminEndpointMapper
{
    /// <summary>
    /// Maps DataSurface administration endpoints under the configured prefix.
    /// </summary>
    /// <param name="app">The route builder to map endpoints onto.</param>
    /// <param name="options">Optional mapping options. If <c>null</c>, defaults are used.</param>
    /// <returns>The original <paramref name="app"/> instance for chaining.</returns>
    public static IEndpointRouteBuilder MapDataSurfaceAdmin(
        this IEndpointRouteBuilder app,
        DataSurfaceAdminOptions? options = null)
    {
        options ??= new DataSurfaceAdminOptions();

        var group = app.MapGroup(options.Prefix);

        if (options.RequireAuthorization)
        {
            if (!string.IsNullOrWhiteSpace(options.Policy)) group.RequireAuthorization(options.Policy);
            else group.RequireAuthorization();
        }

        // Entities
        group.MapGet("/entities", async (DynamicMetadataAdminService svc, HttpContext http, CancellationToken ct) =>
        {
            try { return Results.Ok(await svc.ListEntitiesAsync(ct)); }
            catch (Exception ex) { return DataSurfaceHttpErrorMapper.ToProblem(ex, http); }
        })
            .WithName("DS.Admin.Entities.List")
            .WithTags("DataSurface.Admin");

        group.MapGet("/entities/{entityKey}", async (string entityKey, DynamicMetadataAdminService svc, HttpContext http, CancellationToken ct) =>
        {
            try
            {
                var e = await svc.GetEntityAsync(entityKey, ct);
                return e is null ? Results.NotFound() : Results.Ok(e);
            }
            catch (Exception ex) { return DataSurfaceHttpErrorMapper.ToProblem(ex, http); }
        })
        .WithName("DS.Admin.Entities.Get")
        .WithTags("DataSurface.Admin");

        group.MapPut("/entities/{entityKey}", async (string entityKey, AdminEntityDefDto dto, DynamicMetadataAdminService svc, HttpContext http, CancellationToken ct) =>
        {
            try
            {
                dto.EntityKey = entityKey;
                var (saved, errors) = await svc.UpsertEntityAsync(dto, ct);
                if (errors.Count > 0) return Results.Problem("Validation failed", statusCode: 400, extensions: new Dictionary<string, object?> { ["errors"] = errors });
                return Results.Ok(saved);
            }
            catch (Exception ex) { return DataSurfaceHttpErrorMapper.ToProblem(ex, http); }
        })
        .WithName("DS.Admin.Entities.Upsert")
        .WithTags("DataSurface.Admin");

        group.MapDelete("/entities/{entityKey}", async (string entityKey, DynamicMetadataAdminService svc, HttpContext http, CancellationToken ct) =>
        {
            try
            {
                var ok = await svc.DeleteEntityAsync(entityKey, ct);
                return ok ? Results.NoContent() : Results.NotFound();
            }
            catch (Exception ex) { return DataSurfaceHttpErrorMapper.ToProblem(ex, http); }
        })
        .WithName("DS.Admin.Entities.Delete")
        .WithTags("DataSurface.Admin");

        // Export / Import
        group.MapGet("/export", async (DynamicMetadataAdminService svc, HttpContext http, CancellationToken ct) =>
        {
            try { return Results.Ok(await svc.ExportAsync(ct)); }
            catch (Exception ex) { return DataSurfaceHttpErrorMapper.ToProblem(ex, http); }
        })
            .WithName("DS.Admin.Export")
            .WithTags("DataSurface.Admin");

        group.MapPost("/import", async (AdminImportPayloadDto payload, DynamicMetadataAdminService svc, HttpContext http, CancellationToken ct) =>
        {
            try
            {
                var (imported, errors) = await svc.ImportAsync(payload, ct);
                return Results.Ok(new { imported, errors });
            }
            catch (Exception ex) { return DataSurfaceHttpErrorMapper.ToProblem(ex, http); }
        })
        .WithName("DS.Admin.Import")
        .WithTags("DataSurface.Admin");

        // Index rebuild
        group.MapPost("/entities/{entityKey}/reindex", async (string entityKey, DynamicIndexRebuildService svc, HttpContext http, CancellationToken ct) =>
        {
            try
            {
                var count = await svc.RebuildEntityAsync(entityKey, ct);
                return Results.Ok(new { entityKey, rebuilt = count });
            }
            catch (Exception ex) { return DataSurfaceHttpErrorMapper.ToProblem(ex, http); }
        })
        .WithName("DS.Admin.Reindex")
        .WithTags("DataSurface.Admin");

        return app;
    }
}
