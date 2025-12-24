using System.Text.Json.Nodes;
using DataSurface.Core.Contracts;
using DataSurface.Core.Enums;
using DataSurface.Dynamic.Contracts; // optional but recommended for dynamic catch-all
using DataSurface.EFCore.Contracts;
using DataSurface.EFCore.Interfaces;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace DataSurface.Http;

/// <summary>
/// Extension methods for mapping DataSurface CRUD endpoints into an ASP.NET Core application.
/// </summary>
public static class DataSurfaceEndpointMapper
{
    /// <summary>
    /// Maps DataSurface CRUD endpoints under the configured API prefix.
    /// </summary>
    /// <param name="app">The route builder to map endpoints onto.</param>
    /// <param name="options">Optional mapping options. If <c>null</c>, defaults are used.</param>
    /// <returns>The original <paramref name="app"/> instance for chaining.</returns>
    public static IEndpointRouteBuilder MapDataSurfaceCrud(
        this IEndpointRouteBuilder app,
        DataSurfaceHttpOptions? options = null)
    {
        options ??= new DataSurfaceHttpOptions();

        var group = app.MapGroup(options.ApiPrefix);

        if (options.MapResourceDiscoveryEndpoint)
        {
            DataSurfaceResourceDiscovery.MapDiscovery(group);
            DataSurfaceSchemaEndpoint.MapSchema(group);
        }

        if (options.MapStaticResources)
            MapStatic(group, options);

        if (options.MapDynamicCatchAll)
            MapDynamicCatchAll(group, options);

        return app;
    }

    // ---------------------- Static mapping ----------------------

    private static void MapStatic(RouteGroupBuilder group, DataSurfaceHttpOptions opt)
    {
        using var scope = ((IEndpointRouteBuilder) group).ServiceProvider.CreateScope();
        var provider = scope.ServiceProvider.GetRequiredService<IResourceContractProvider>();

        // route collision guard
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var c in provider.All.Where(x => x.Backend != StorageBackend.DynamicJson))
        {
            var route = "/" + c.Route.Trim('/');

            if (!used.Add(route) && opt.ThrowOnRouteCollision)
                throw new InvalidOperationException($"Duplicate static route '{route}'.");

            MapCrudForContract(group, c, opt, route);
        }
    }

    // ---------------------- Dynamic catch-all ----------------------

    private static void MapDynamicCatchAll(RouteGroupBuilder group, DataSurfaceHttpOptions opt)
    {
        // /api/d/{route} and /api/d/{route}/{id}
        var dyn = group.MapGroup(opt.DynamicPrefix);

        // LIST dynamic: GET /api/d/{route}?...
        dyn.MapGet("/{route}", async (string route, HttpRequest req, HttpResponse res, IServiceProvider sp, CancellationToken ct) =>
        {
            try
            {
                var dynProvider = sp.GetRequiredService<DynamicResourceContractProvider>();
                var contract = await dynProvider.TryGetByRouteAsync(route, ct);
                if (contract is null) return Results.NotFound();

                return await HandleList(contract, req, res, sp, opt, ct);
            }
            catch (Exception ex)
            {
                return DataSurfaceHttpErrorMapper.ToProblem(ex, req.HttpContext);
            }
        })
        .WithName("DataSurface.Dynamic.List")
        .WithTags("Dynamic")
        .WithMetadata(new DataSurfaceCrudEndpointMetadata("*", CrudOperation.List));

        // GET dynamic: GET /api/d/{route}/{id}
        dyn.MapGet("/{route}/{id}", async (string route, string id, HttpRequest req, HttpResponse res, IServiceProvider sp, CancellationToken ct) =>
        {
            try
            {
                var dynProvider = sp.GetRequiredService<DynamicResourceContractProvider>();
                var contract = await dynProvider.TryGetByRouteAsync(route, ct);
                if (contract is null) return Results.NotFound();

                return await HandleGet(contract, id, req, res, sp, opt, ct);
            }
            catch (Exception ex)
            {
                return DataSurfaceHttpErrorMapper.ToProblem(ex, req.HttpContext);
            }
        })
        .WithName("DataSurface.Dynamic.Get")
        .WithTags("Dynamic")
        .WithMetadata(new DataSurfaceCrudEndpointMetadata("*", CrudOperation.Get));

        // CREATE dynamic
        dyn.MapPost("/{route}", async (string route, JsonObject body, HttpRequest req, HttpResponse res, IServiceProvider sp, CancellationToken ct) =>
        {
            try
            {
                var dynProvider = sp.GetRequiredService<DynamicResourceContractProvider>();
                var contract = await dynProvider.TryGetByRouteAsync(route, ct);
                if (contract is null) return Results.NotFound();

                return await HandleCreate(contract, body, req, res, sp, opt, ct);
            }
            catch (Exception ex)
            {
                return DataSurfaceHttpErrorMapper.ToProblem(ex, req.HttpContext);
            }
        })
        .WithName("DataSurface.Dynamic.Create")
        .WithTags("Dynamic")
        .WithMetadata(new DataSurfaceCrudEndpointMetadata("*", CrudOperation.Create));

        // UPDATE dynamic
        dyn.MapMethods("/{route}/{id}", new[] { "PATCH" }, async (string route, string id, JsonObject patch, HttpRequest req, HttpResponse res, IServiceProvider sp, CancellationToken ct) =>
        {
            try
            {
                var dynProvider = sp.GetRequiredService<DynamicResourceContractProvider>();
                var contract = await dynProvider.TryGetByRouteAsync(route, ct);
                if (contract is null) return Results.NotFound();

                return await HandleUpdate(contract, id, patch, req, res, sp, opt, ct);
            }
            catch (Exception ex)
            {
                return DataSurfaceHttpErrorMapper.ToProblem(ex, req.HttpContext);
            }
        })
        .WithName("DataSurface.Dynamic.Update")
        .WithTags("Dynamic")
        .WithMetadata(new DataSurfaceCrudEndpointMetadata("*", CrudOperation.Update));

        // DELETE dynamic
        dyn.MapDelete("/{route}/{id}", async (string route, string id, HttpRequest req, IServiceProvider sp, CancellationToken ct) =>
        {
            try
            {
                var dynProvider = sp.GetRequiredService<DynamicResourceContractProvider>();
                var contract = await dynProvider.TryGetByRouteAsync(route, ct);
                if (contract is null) return Results.NotFound();

                return await HandleDelete(contract, id, req, sp, opt, ct);
            }
            catch (Exception ex)
            {
                return DataSurfaceHttpErrorMapper.ToProblem(ex, req.HttpContext);
            }
        })
        .WithName("DataSurface.Dynamic.Delete")
        .WithTags("Dynamic")
        .WithMetadata(new DataSurfaceCrudEndpointMetadata("*", CrudOperation.Delete));
    }

    // ---------------------- Static handler mapping per contract ----------------------

    private static void MapCrudForContract(RouteGroupBuilder group, ResourceContract c, DataSurfaceHttpOptions opt, string route)
    {
        // LIST
        if (c.Operations[CrudOperation.List].Enabled)
        {
            var ep = group.MapGet(route, async (HttpRequest req, HttpResponse res, IServiceProvider sp, CancellationToken ct) =>
            {
                try { return await HandleList(c, req, res, sp, opt, ct); }
                catch (Exception ex) { return DataSurfaceHttpErrorMapper.ToProblem(ex, req.HttpContext); }
            })
            .WithTags(c.Route)
            .WithName($"{c.Route}.list")
            .WithMetadata(new DataSurfaceCrudEndpointMetadata(c.ResourceKey, CrudOperation.List));

            ApplyAuth(ep, c, CrudOperation.List, opt);

            // HEAD - returns count in header without body
            var headEp = group.MapMethods(route, new[] { "HEAD" }, async (HttpRequest req, HttpResponse res, IServiceProvider sp, CancellationToken ct) =>
            {
                try { return await HandleHead(c, req, res, sp, opt, ct); }
                catch (Exception ex) { return DataSurfaceHttpErrorMapper.ToProblem(ex, req.HttpContext); }
            })
            .WithTags(c.Route)
            .WithName($"{c.Route}.head")
            .WithMetadata(new DataSurfaceCrudEndpointMetadata(c.ResourceKey, CrudOperation.List));

            ApplyAuth(headEp, c, CrudOperation.List, opt);
        }

        // GET
        if (c.Operations[CrudOperation.Get].Enabled)
        {
            var ep = group.MapGet($"{route}/{{id}}", async (string id, HttpRequest req, HttpResponse res, IServiceProvider sp, CancellationToken ct) =>
            {
                try { return await HandleGet(c, id, req, res, sp, opt, ct); }
                catch (Exception ex) { return DataSurfaceHttpErrorMapper.ToProblem(ex, req.HttpContext); }
            })
            .WithTags(c.Route)
            .WithName($"{c.Route}.get")
            .WithMetadata(new DataSurfaceCrudEndpointMetadata(c.ResourceKey, CrudOperation.Get));

            ApplyAuth(ep, c, CrudOperation.Get, opt);
        }

        // CREATE
        if (c.Operations[CrudOperation.Create].Enabled)
        {
            var ep = group.MapPost(route, async (JsonObject body, HttpRequest req, HttpResponse res, IServiceProvider sp, CancellationToken ct) =>
            {
                try { return await HandleCreate(c, body, req, res, sp, opt, ct); }
                catch (Exception ex) { return DataSurfaceHttpErrorMapper.ToProblem(ex, req.HttpContext); }
            })
            .WithTags(c.Route)
            .WithName($"{c.Route}.create")
            .WithMetadata(new DataSurfaceCrudEndpointMetadata(c.ResourceKey, CrudOperation.Create));

            ApplyAuth(ep, c, CrudOperation.Create, opt);
        }

        // UPDATE
        if (c.Operations[CrudOperation.Update].Enabled)
        {
            var ep = group.MapMethods($"{route}/{{id}}", new[] { "PATCH" }, async (string id, JsonObject patch, HttpRequest req, HttpResponse res, IServiceProvider sp, CancellationToken ct) =>
            {
                try { return await HandleUpdate(c, id, patch, req, res, sp, opt, ct); }
                catch (Exception ex) { return DataSurfaceHttpErrorMapper.ToProblem(ex, req.HttpContext); }
            })
            .WithTags(c.Route)
            .WithName($"{c.Route}.update")
            .WithMetadata(new DataSurfaceCrudEndpointMetadata(c.ResourceKey, CrudOperation.Update));

            ApplyAuth(ep, c, CrudOperation.Update, opt);
        }

        // DELETE
        if (c.Operations[CrudOperation.Delete].Enabled)
        {
            var ep = group.MapDelete($"{route}/{{id}}", async (string id, HttpRequest req, IServiceProvider sp, CancellationToken ct) =>
            {
                try { return await HandleDelete(c, id, req, sp, opt, ct); }
                catch (Exception ex) { return DataSurfaceHttpErrorMapper.ToProblem(ex, req.HttpContext); }
            })
            .WithTags(c.Route)
            .WithName($"{c.Route}.delete")
            .WithMetadata(new DataSurfaceCrudEndpointMetadata(c.ResourceKey, CrudOperation.Delete));

            ApplyAuth(ep, c, CrudOperation.Delete, opt);
        }

        // BULK - POST /api/{resource}/bulk
        if (opt.EnableBulkOperations)
        {
            var ep = group.MapPost($"{route}/bulk", async (BulkOperationSpec spec, HttpRequest req, IServiceProvider sp, CancellationToken ct) =>
            {
                try { return await HandleBulk(c, spec, req, sp, opt, ct); }
                catch (Exception ex) { return DataSurfaceHttpErrorMapper.ToProblem(ex, req.HttpContext); }
            })
            .WithTags(c.Route)
            .WithName($"{c.Route}.bulk")
            .WithMetadata(new DataSurfaceCrudEndpointMetadata(c.ResourceKey, CrudOperation.Create));

            ApplyAuth(ep, c, CrudOperation.Create, opt);
        }

        // STREAM - GET /api/{resource}/stream
        if (opt.EnableStreaming && c.Operations[CrudOperation.List].Enabled)
        {
            var ep = group.MapGet($"{route}/stream", (HttpRequest req, HttpResponse res, IServiceProvider sp, CancellationToken ct) =>
            {
                try { return HandleStream(c, req, res, sp, opt, ct); }
                catch (Exception ex) { return DataSurfaceHttpErrorMapper.ToProblem(ex, req.HttpContext); }
            })
            .WithTags(c.Route)
            .WithName($"{c.Route}.stream")
            .WithMetadata(new DataSurfaceCrudEndpointMetadata(c.ResourceKey, CrudOperation.List));

            ApplyAuth(ep, c, CrudOperation.List, opt);
        }

        // PUT - Full replacement update (optional)
        if (opt.EnablePutForFullUpdate && c.Operations[CrudOperation.Update].Enabled)
        {
            var ep = group.MapPut($"{route}/{{id}}", async (string id, JsonObject body, HttpRequest req, HttpResponse res, IServiceProvider sp, CancellationToken ct) =>
            {
                try { return await HandlePut(c, id, body, req, res, sp, opt, ct); }
                catch (Exception ex) { return DataSurfaceHttpErrorMapper.ToProblem(ex, req.HttpContext); }
            })
            .WithTags(c.Route)
            .WithName($"{c.Route}.put")
            .WithMetadata(new DataSurfaceCrudEndpointMetadata(c.ResourceKey, CrudOperation.Update));

            ApplyAuth(ep, c, CrudOperation.Update, opt);
        }

        // EXPORT - GET /api/{resource}/export
        if (opt.EnableImportExport && c.Operations[CrudOperation.List].Enabled)
        {
            var ep = group.MapGet($"{route}/export", async (HttpRequest req, HttpResponse res, IServiceProvider sp, CancellationToken ct) =>
            {
                try { return await HandleExport(c, req, res, sp, opt, ct); }
                catch (Exception ex) { return DataSurfaceHttpErrorMapper.ToProblem(ex, req.HttpContext); }
            })
            .WithTags(c.Route)
            .WithName($"{c.Route}.export")
            .WithMetadata(new DataSurfaceCrudEndpointMetadata(c.ResourceKey, CrudOperation.List));

            ApplyAuth(ep, c, CrudOperation.List, opt);
        }

        // IMPORT - POST /api/{resource}/import
        if (opt.EnableImportExport && c.Operations[CrudOperation.Create].Enabled)
        {
            var ep = group.MapPost($"{route}/import", async (HttpRequest req, HttpResponse res, IServiceProvider sp, CancellationToken ct) =>
            {
                try { return await HandleImport(c, req, res, sp, opt, ct); }
                catch (Exception ex) { return DataSurfaceHttpErrorMapper.ToProblem(ex, req.HttpContext); }
            })
            .WithTags(c.Route)
            .WithName($"{c.Route}.import")
            .WithMetadata(new DataSurfaceCrudEndpointMetadata(c.ResourceKey, CrudOperation.Create));

            ApplyAuth(ep, c, CrudOperation.Create, opt);
        }
    }

    private static void ApplyAuth(RouteHandlerBuilder ep, ResourceContract c, CrudOperation op, DataSurfaceHttpOptions opt)
    {
        if (c.Security.Policies.TryGetValue(op, out var policy) && !string.IsNullOrWhiteSpace(policy))
        {
            ep.RequireAuthorization(policy);
            return;
        }

        if (opt.RequireAuthorizationByDefault)
        {
            if (!string.IsNullOrWhiteSpace(opt.DefaultPolicy))
                ep.RequireAuthorization(opt.DefaultPolicy);
            else
                ep.RequireAuthorization();
        }
    }

    // ---------------------- CRUD handlers (shared) ----------------------

    private static async Task<IResult> HandleList(ResourceContract c, HttpRequest req, HttpResponse res, IServiceProvider sp, DataSurfaceHttpOptions opt, CancellationToken ct)
    {
        var crud = sp.GetRequiredService<IDataSurfaceCrudService>();

        var spec = DataSurfaceQueryParser.ParseQuerySpec(req, c);
        var expand = DataSurfaceQueryParser.ParseExpand(req, c);

        var result = await crud.ListAsync(c.ResourceKey, spec, expand, ct);

        // Set count headers for client convenience
        res.Headers["X-Total-Count"] = result.Total.ToString();
        res.Headers["X-Page"] = result.Page.ToString();
        res.Headers["X-Page-Size"] = result.PageSize.ToString();

        // Set Cache-Control header if configured
        if (opt.CacheControlMaxAgeSeconds > 0)
        {
            res.Headers.CacheControl = $"max-age={opt.CacheControlMaxAgeSeconds}";
        }

        return Results.Ok(result);
    }

    private static async Task<IResult> HandleHead(ResourceContract c, HttpRequest req, HttpResponse res, IServiceProvider sp, DataSurfaceHttpOptions opt, CancellationToken ct)
    {
        var crud = sp.GetRequiredService<IDataSurfaceCrudService>();

        // Use minimal page size since we only need the count
        var spec = DataSurfaceQueryParser.ParseQuerySpec(req, c);
        spec = spec with { PageSize = 1 };

        var result = await crud.ListAsync(c.ResourceKey, spec, expand: null, ct);

        res.Headers["X-Total-Count"] = result.Total.ToString();
        res.Headers["X-Page"] = result.Page.ToString();
        res.Headers["X-Page-Size"] = c.Query.MaxPageSize.ToString();

        // Set Cache-Control header if configured
        if (opt.CacheControlMaxAgeSeconds > 0)
        {
            res.Headers.CacheControl = $"max-age={opt.CacheControlMaxAgeSeconds}";
        }

        return Results.Ok();
    }

    private static async Task<IResult> HandleGet(ResourceContract c, string id, HttpRequest req, HttpResponse res, IServiceProvider sp, DataSurfaceHttpOptions opt, CancellationToken ct)
    {
        var crud = sp.GetRequiredService<IDataSurfaceCrudService>();

        var expand = DataSurfaceQueryParser.ParseExpand(req, c);
        var keyObj = ParseId(id, c);

        var obj = await crud.GetAsync(c.ResourceKey, keyObj, expand, ct);
        if (obj is null) return Results.NotFound();

        // Set ETag and check for conditional GET (304 Not Modified)
        var etag = DataSurfaceHttpEtags.TrySetEtag(res, c, obj, opt.EnableEtags);
        if (opt.EnableConditionalGet && etag is not null)
        {
            var ifNoneMatch = req.Headers.IfNoneMatch.FirstOrDefault();
            if (ifNoneMatch is not null && ifNoneMatch.Trim('"') == etag.Trim('"'))
            {
                return Results.StatusCode(304);
            }
        }

        // Set Cache-Control header if configured
        if (opt.CacheControlMaxAgeSeconds > 0)
        {
            res.Headers.CacheControl = $"max-age={opt.CacheControlMaxAgeSeconds}";
        }

        return Results.Ok(obj);
    }

    private static async Task<IResult> HandleCreate(ResourceContract c, JsonObject body, HttpRequest req, HttpResponse res, IServiceProvider sp, DataSurfaceHttpOptions opt, CancellationToken ct)
    {
        var crud = sp.GetRequiredService<IDataSurfaceCrudService>();

        var created = await crud.CreateAsync(c.ResourceKey, body, ct);

        DataSurfaceHttpEtags.TrySetEtag(res, c, created, opt.EnableEtags);

        // Location
        var keyApi = GetKeyApiName(c);
        if (created.TryGetPropertyValue(keyApi, out var idNode) && idNode != null)
        {
            var idVal = idNode.ToJsonString().Trim('"');
            return Results.Created($"{req.Scheme}://{req.Host}{req.Path}/{idVal}", created);
        }

        return Results.Created(req.Path, created);
    }

    private static async Task<IResult> HandleUpdate(ResourceContract c, string id, JsonObject patch, HttpRequest req, HttpResponse res, IServiceProvider sp, DataSurfaceHttpOptions opt, CancellationToken ct)
    {
        var crud = sp.GetRequiredService<IDataSurfaceCrudService>();

        // If-Match -> concurrency token (RowVersion)
        DataSurfaceHttpEtags.ApplyIfMatchToPatch(c, req, patch, opt.EnableEtags);

        var keyObj = ParseId(id, c);
        var updated = await crud.UpdateAsync(c.ResourceKey, keyObj, patch, ct);

        DataSurfaceHttpEtags.TrySetEtag(res, c, updated, opt.EnableEtags);
        return Results.Ok(updated);
    }

    private static async Task<IResult> HandleDelete(ResourceContract c, string id, HttpRequest req, IServiceProvider sp, DataSurfaceHttpOptions opt, CancellationToken ct)
    {
        var crud = sp.GetRequiredService<IDataSurfaceCrudService>();

        var keyObj = ParseId(id, c);

        // Extract If-Match token for concurrency check
        CrudDeleteSpec? deleteSpec = null;
        if (opt.EnableEtags)
        {
            var token = DataSurfaceHttpEtags.GetIfMatchToken(req);
            if (!string.IsNullOrWhiteSpace(token))
                deleteSpec = new CrudDeleteSpec(HardDelete: false, ConcurrencyToken: token);
        }

        await crud.DeleteAsync(c.ResourceKey, keyObj, deleteSpec, ct);
        return Results.NoContent();
    }

    private static async Task<IResult> HandleBulk(ResourceContract c, BulkOperationSpec spec, HttpRequest req, IServiceProvider sp, DataSurfaceHttpOptions opt, CancellationToken ct)
    {
        var bulk = sp.GetRequiredService<IDataSurfaceBulkService>();
        var result = await bulk.ExecuteAsync(c.ResourceKey, spec, ct);

        if (result.Success)
            return Results.Ok(result);

        // Return 207 Multi-Status for partial failures
        return Results.Json(result, statusCode: 207);
    }

    private static IResult HandleStream(ResourceContract c, HttpRequest req, HttpResponse res, IServiceProvider sp, DataSurfaceHttpOptions opt, CancellationToken ct)
    {
        var streaming = sp.GetRequiredService<IDataSurfaceStreamingService>();

        var spec = DataSurfaceQueryParser.ParseQuerySpec(req, c);
        var expand = DataSurfaceQueryParser.ParseExpand(req, c);

        res.Headers.ContentType = "application/x-ndjson";
        
        return Results.Stream(async stream =>
        {
            var writer = new System.IO.StreamWriter(stream);
            await foreach (var item in streaming.StreamAsync(c.ResourceKey, spec, expand, ct))
            {
                await writer.WriteLineAsync(item.ToJsonString());
                await writer.FlushAsync();
            }
        }, contentType: "application/x-ndjson");
    }

    private static async Task<IResult> HandlePut(ResourceContract c, string id, JsonObject body, HttpRequest req, HttpResponse res, IServiceProvider sp, DataSurfaceHttpOptions opt, CancellationToken ct)
    {
        var crud = sp.GetRequiredService<IDataSurfaceCrudService>();

        // If-Match -> concurrency token (RowVersion)
        DataSurfaceHttpEtags.ApplyIfMatchToPatch(c, req, body, opt.EnableEtags);

        var keyObj = ParseId(id, c);

        // For PUT, we need to ensure all required fields are present (full replacement)
        // The update service will handle validation
        var updated = await crud.UpdateAsync(c.ResourceKey, keyObj, body, ct);

        DataSurfaceHttpEtags.TrySetEtag(res, c, updated, opt.EnableEtags);
        return Results.Ok(updated);
    }

    private static async Task<IResult> HandleExport(ResourceContract c, HttpRequest req, HttpResponse res, IServiceProvider sp, DataSurfaceHttpOptions opt, CancellationToken ct)
    {
        var crud = sp.GetRequiredService<IDataSurfaceCrudService>();

        // Get format from query string (default: json)
        var format = req.Query.TryGetValue("format", out var f) && f.ToString().Equals("csv", StringComparison.OrdinalIgnoreCase)
            ? "csv"
            : "json";

        // Export all records (with pagination limits)
        var spec = DataSurfaceQueryParser.ParseQuerySpec(req, c);
        spec = spec with { PageSize = c.Query.MaxPageSize };

        var result = await crud.ListAsync(c.ResourceKey, spec, expand: null, ct);

        if (format == "csv")
        {
            res.Headers.ContentType = "text/csv";
            res.Headers.ContentDisposition = $"attachment; filename=\"{c.ResourceKey}_export.csv\"";

            var fields = c.Fields.Where(field => field.InRead && !field.Hidden).ToList();
            var csv = new System.Text.StringBuilder();

            // Header row
            csv.AppendLine(string.Join(",", fields.Select(field => $"\"{field.ApiName}\"")));

            // Data rows
            foreach (var item in result.Items)
            {
                var values = fields.Select(field =>
                {
                    if (item.TryGetPropertyValue(field.ApiName, out var val) && val != null)
                    {
                        var str = val.ToString().Replace("\"", "\"\"");
                        return $"\"{str}\"";
                    }
                    return "\"\"";
                });
                csv.AppendLine(string.Join(",", values));
            }

            return Results.Text(csv.ToString(), "text/csv");
        }

        res.Headers.ContentType = "application/json";
        res.Headers.ContentDisposition = $"attachment; filename=\"{c.ResourceKey}_export.json\"";
        return Results.Ok(result.Items);
    }

    private static async Task<IResult> HandleImport(ResourceContract c, HttpRequest req, HttpResponse res, IServiceProvider sp, DataSurfaceHttpOptions opt, CancellationToken ct)
    {
        var crud = sp.GetRequiredService<IDataSurfaceCrudService>();

        // Read request body as JSON array
        using var reader = new System.IO.StreamReader(req.Body);
        var bodyText = await reader.ReadToEndAsync(ct);

        var items = System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.Nodes.JsonArray>(bodyText);
        if (items == null || items.Count == 0)
            return Results.BadRequest(new { error = "Request body must be a non-empty JSON array." });

        var successCount = 0;
        var failureCount = 0;
        var errors = new List<object>();

        var rowNum = 0;
        foreach (var item in items)
        {
            rowNum++;
            if (item is not JsonObject obj)
            {
                failureCount++;
                errors.Add(new { row = rowNum, error = "Item is not a valid JSON object." });
                continue;
            }

            try
            {
                await crud.CreateAsync(c.ResourceKey, obj, ct);
                successCount++;
            }
            catch (Exception ex)
            {
                failureCount++;
                errors.Add(new { row = rowNum, error = ex.Message });
            }
        }

        return Results.Ok(new
        {
            total = items.Count,
            success = successCount,
            failures = failureCount,
            errors
        });
    }

    // ---------------------- helpers ----------------------

    private static object ParseId(string raw, ResourceContract c)
    {
        return c.Key.Type switch
        {
            FieldType.Int32 => int.Parse(raw),
            FieldType.Int64 => long.Parse(raw),
            FieldType.Guid => Guid.Parse(raw),
            FieldType.String => raw,
            _ => raw
        };
    }

    private static string GetKeyApiName(ResourceContract c)
    {
        var keyField = c.Fields.FirstOrDefault(f => f.Name.Equals(c.Key.Name, StringComparison.OrdinalIgnoreCase));
        return keyField?.ApiName ?? char.ToLowerInvariant(c.Key.Name[0]) + c.Key.Name[1..];
    }
}
