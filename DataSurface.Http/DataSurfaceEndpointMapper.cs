using System.Text.Json.Nodes;
using DataSurface.Core;
using DataSurface.Core.Contracts;
using DataSurface.Core.Enums;
using DataSurface.Dynamic.Contracts; // optional but recommended for dynamic catch-all
using DataSurface.EFCore.Contracts;
using DataSurface.EFCore.Exceptions;
using DataSurface.EFCore.Interfaces;
using Microsoft.AspNetCore.Authorization;
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

        // Fail fast: API key auth without a validator would accept any non-empty key (presence-only,
        // not real validation), which is a security footgun. Require an IApiKeyValidator to be registered.
        // Uses IServiceProviderIsService rather than resolving, so a (typical) scoped validator does not
        // blow up under root-scope validation at startup.
        if (options.EnableApiKeyAuth && !IsApiKeyValidatorRegistered(app.ServiceProvider))
            throw new InvalidOperationException(
                "EnableApiKeyAuth is true but no IApiKeyValidator is registered. Register one " +
                "(e.g. services.AddScoped<IApiKeyValidator, MyValidator>()) so API keys are validated — " +
                "without a validator, any non-empty key would be accepted.");

        var group = app.MapGroup(options.ApiPrefix);

        // Metadata endpoints (schema/discovery) expose the full data model, so they participate in
        // the same default authorization / API key / rate limiting as the CRUD endpoints.
        if (options.MapSchemaEndpoint || options.MapResourceDiscoveryEndpoint)
        {
            var meta = group.MapGroup("");
            ApplyGroupBaseAuth(meta, options);

            if (options.MapSchemaEndpoint)
                DataSurfaceSchemaEndpoint.MapSchema(meta);

            if (options.MapResourceDiscoveryEndpoint)
                DataSurfaceResourceDiscovery.MapDiscovery(meta);
        }

        if (options.MapStaticResources)
            MapStatic(group, options);

        if (options.MapDynamicCatchAll)
            MapDynamicCatchAll(group, options);

        return app;
    }

    private static bool IsApiKeyValidatorRegistered(IServiceProvider sp)
    {
        var probe = sp.GetService<IServiceProviderIsService>();
        if (probe is not null)
            return probe.IsService(typeof(IApiKeyValidator));

        // Container without IServiceProviderIsService support: fall back to resolving inside a scope.
        using var scope = sp.CreateScope();
        return scope.ServiceProvider.GetService<IApiKeyValidator>() is not null;
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

        // The dynamic catch-all otherwise bypasses ApplyAuth's rate-limiting/API-key (authorization itself
        // is enforced per-request in EnforceDynamicPolicyAsync), so attach those to the whole group here.
        ApplyDynamicAuth(dyn, opt);

        // LIST dynamic: GET /api/d/{route}?...
        dyn.MapGet("/{route}", async (string route, HttpRequest req, HttpResponse res, IServiceProvider sp, CancellationToken ct) =>
        {
            try
            {
                var pre = PreAuthDynamic(req.HttpContext, opt);
                if (pre is not null) return pre;

                var dynProvider = sp.GetRequiredService<DynamicResourceContractProvider>();
                var contract = await dynProvider.TryGetByRouteAsync(route, ct);
                if (contract is null) return Results.NotFound();

                var deny = await EnforceDynamicPolicyAsync(contract, CrudOperation.List, req.HttpContext, opt);
                if (deny is not null) return deny;

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

        // HEAD dynamic: HEAD /api/d/{route} (count only)
        dyn.MapMethods("/{route}", new[] { "HEAD" }, async (string route, HttpRequest req, HttpResponse res, IServiceProvider sp, CancellationToken ct) =>
        {
            try
            {
                var pre = PreAuthDynamic(req.HttpContext, opt);
                if (pre is not null) return pre;

                var dynProvider = sp.GetRequiredService<DynamicResourceContractProvider>();
                var contract = await dynProvider.TryGetByRouteAsync(route, ct);
                if (contract is null) return Results.NotFound();

                var deny = await EnforceDynamicPolicyAsync(contract, CrudOperation.List, req.HttpContext, opt);
                if (deny is not null) return deny;

                return await HandleHead(contract, req, res, sp, opt, ct);
            }
            catch (Exception ex)
            {
                return DataSurfaceHttpErrorMapper.ToProblem(ex, req.HttpContext);
            }
        })
        .WithName("DataSurface.Dynamic.Head")
        .WithTags("Dynamic")
        .WithMetadata(new DataSurfaceCrudEndpointMetadata("*", CrudOperation.List, DataSurfaceEndpointKind.Head));

        // GET dynamic: GET /api/d/{route}/{id}
        dyn.MapGet("/{route}/{id}", async (string route, string id, HttpRequest req, HttpResponse res, IServiceProvider sp, CancellationToken ct) =>
        {
            try
            {
                var pre = PreAuthDynamic(req.HttpContext, opt);
                if (pre is not null) return pre;

                var dynProvider = sp.GetRequiredService<DynamicResourceContractProvider>();
                var contract = await dynProvider.TryGetByRouteAsync(route, ct);
                if (contract is null) return Results.NotFound();

                var deny = await EnforceDynamicPolicyAsync(contract, CrudOperation.Get, req.HttpContext, opt);
                if (deny is not null) return deny;

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
                var pre = PreAuthDynamic(req.HttpContext, opt);
                if (pre is not null) return pre;

                var dynProvider = sp.GetRequiredService<DynamicResourceContractProvider>();
                var contract = await dynProvider.TryGetByRouteAsync(route, ct);
                if (contract is null) return Results.NotFound();

                var deny = await EnforceDynamicPolicyAsync(contract, CrudOperation.Create, req.HttpContext, opt);
                if (deny is not null) return deny;

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
                var pre = PreAuthDynamic(req.HttpContext, opt);
                if (pre is not null) return pre;

                var dynProvider = sp.GetRequiredService<DynamicResourceContractProvider>();
                var contract = await dynProvider.TryGetByRouteAsync(route, ct);
                if (contract is null) return Results.NotFound();

                var deny = await EnforceDynamicPolicyAsync(contract, CrudOperation.Update, req.HttpContext, opt);
                if (deny is not null) return deny;

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
                var pre = PreAuthDynamic(req.HttpContext, opt);
                if (pre is not null) return pre;

                var dynProvider = sp.GetRequiredService<DynamicResourceContractProvider>();
                var contract = await dynProvider.TryGetByRouteAsync(route, ct);
                if (contract is null) return Results.NotFound();

                var deny = await EnforceDynamicPolicyAsync(contract, CrudOperation.Delete, req.HttpContext, opt);
                if (deny is not null) return deny;

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
            .WithMetadata(new DataSurfaceCrudEndpointMetadata(c.ResourceKey, CrudOperation.List, DataSurfaceEndpointKind.Head));

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
            .WithMetadata(new DataSurfaceCrudEndpointMetadata(c.ResourceKey, CrudOperation.Create, DataSurfaceEndpointKind.Bulk));

            // Bulk spans Create/Update/Delete: the endpoint carries only the base (default)
            // authorization; HandleBulk enforces the per-operation policies for whichever
            // sections of the spec are non-empty.
            ApplyBaseAuth(ep, opt);
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
            .WithMetadata(new DataSurfaceCrudEndpointMetadata(c.ResourceKey, CrudOperation.List, DataSurfaceEndpointKind.Stream));

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
            .WithMetadata(new DataSurfaceCrudEndpointMetadata(c.ResourceKey, CrudOperation.List, DataSurfaceEndpointKind.Export));

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
            .WithMetadata(new DataSurfaceCrudEndpointMetadata(c.ResourceKey, CrudOperation.Create, DataSurfaceEndpointKind.Import));

            ApplyAuth(ep, c, CrudOperation.Create, opt);
        }
    }

    private static void ApplyAuth(RouteHandlerBuilder ep, ResourceContract c, CrudOperation op, DataSurfaceHttpOptions opt)
    {
        // Authorization
        if (c.Security.Policies.TryGetValue(op, out var policy) && !string.IsNullOrWhiteSpace(policy))
        {
            ep.RequireAuthorization(policy);
        }
        else if (opt.RequireAuthorizationByDefault)
        {
            if (!string.IsNullOrWhiteSpace(opt.DefaultPolicy))
                ep.RequireAuthorization(opt.DefaultPolicy);
            else
                ep.RequireAuthorization();
        }

        // Rate limiting
        if (opt.EnableRateLimiting && !string.IsNullOrWhiteSpace(opt.RateLimitingPolicy))
        {
            ep.RequireRateLimiting(opt.RateLimitingPolicy);
        }

        // API key authentication
        if (opt.EnableApiKeyAuth)
            ep.AddEndpointFilterFactory(ApiKeyFilterFactory(opt));
    }

    // Applies the default authorization, rate-limiting and API-key concerns to an endpoint
    // WITHOUT a per-operation resource policy (used for bulk — which spans Create/Update/Delete
    // and enforces per-section policies per request — and for metadata endpoints).
    private static void ApplyBaseAuth(RouteHandlerBuilder ep, DataSurfaceHttpOptions opt)
    {
        if (opt.RequireAuthorizationByDefault)
        {
            if (!string.IsNullOrWhiteSpace(opt.DefaultPolicy))
                ep.RequireAuthorization(opt.DefaultPolicy);
            else
                ep.RequireAuthorization();
        }

        if (opt.EnableRateLimiting && !string.IsNullOrWhiteSpace(opt.RateLimitingPolicy))
            ep.RequireRateLimiting(opt.RateLimitingPolicy);

        if (opt.EnableApiKeyAuth)
            ep.AddEndpointFilterFactory(ApiKeyFilterFactory(opt));
    }

    // Group-level variant of ApplyBaseAuth (metadata endpoints).
    private static void ApplyGroupBaseAuth(RouteGroupBuilder g, DataSurfaceHttpOptions opt)
    {
        if (opt.RequireAuthorizationByDefault)
        {
            if (!string.IsNullOrWhiteSpace(opt.DefaultPolicy))
                g.RequireAuthorization(opt.DefaultPolicy);
            else
                g.RequireAuthorization();
        }

        if (opt.EnableRateLimiting && !string.IsNullOrWhiteSpace(opt.RateLimitingPolicy))
            g.RequireRateLimiting(opt.RateLimitingPolicy);

        if (opt.EnableApiKeyAuth)
            g.AddEndpointFilterFactory(ApiKeyFilterFactory(opt));
    }

    // The validator is resolved from the REQUEST services on every invocation: the filter factory
    // runs once at pipeline build, and resolving there from the root provider would either throw
    // for a scoped validator (the typical DbContext-backed case) or capture it for the app's
    // lifetime as an unintended singleton.
    private static Func<EndpointFilterFactoryContext, EndpointFilterDelegate, EndpointFilterDelegate> ApiKeyFilterFactory(DataSurfaceHttpOptions opt)
        => (context, next) => invocationContext =>
        {
            var validator = invocationContext.HttpContext.RequestServices.GetService<IApiKeyValidator>();
            var filter = new DataSurfaceApiKeyFilter(opt, validator);
            return filter.InvokeAsync(invocationContext, next);
        };

    // Applies the rate-limiting and API-key concerns to the dynamic catch-all group. Authorization
    // (per-resource policy OR the default requirement) is enforced per-request in EnforceDynamicPolicyAsync,
    // because the resource — and thus its policy — is only known per request.
    private static void ApplyDynamicAuth(RouteGroupBuilder dyn, DataSurfaceHttpOptions opt)
    {
        if (opt.EnableRateLimiting && !string.IsNullOrWhiteSpace(opt.RateLimitingPolicy))
            dyn.RequireRateLimiting(opt.RateLimitingPolicy);

        if (opt.EnableApiKeyAuth)
            dyn.AddEndpointFilterFactory(ApiKeyFilterFactory(opt));
    }

    // Anonymous requests must not be able to distinguish existing dynamic resources (401) from
    // non-existent ones (404). When default authorization is on, deny before the route lookup.
    private static IResult? PreAuthDynamic(HttpContext http, DataSurfaceHttpOptions opt)
    {
        if (!opt.RequireAuthorizationByDefault) return null;
        var authenticated = http.User.Identity?.IsAuthenticated ?? false;
        return authenticated ? null : Results.Challenge();
    }

    // Enforces a resource's per-operation authorization policy on the dynamic catch-all routes,
    // where a static .RequireAuthorization(policy) cannot be attached at map-time (the resource is
    // only known per request). Returns a deny result (403 if authenticated, 401 otherwise) or null
    // when the request is allowed / no policy applies.
    private static async Task<IResult?> EnforceDynamicPolicyAsync(ResourceContract c, CrudOperation op, HttpContext http, DataSurfaceHttpOptions opt)
    {
        // Mirror static ApplyAuth: a resource's own per-operation policy takes precedence; otherwise fall
        // back to the default authorization requirement. Never require both.
        string? policy;
        if (c.Security.Policies.TryGetValue(op, out var resourcePolicy) && !string.IsNullOrWhiteSpace(resourcePolicy))
            policy = resourcePolicy;
        else if (opt.RequireAuthorizationByDefault)
            policy = opt.DefaultPolicy; // may be null → require only an authenticated user
        else
            return null; // no authorization required

        var authenticated = http.User.Identity?.IsAuthenticated ?? false;

        if (!string.IsNullOrWhiteSpace(policy))
        {
            var authService = http.RequestServices.GetService<IAuthorizationService>();
            if (authService is null)
                return Results.Problem(
                    "This resource requires an authorization policy, but authorization services are not configured (call AddAuthorization()).",
                    statusCode: StatusCodes.Status500InternalServerError);

            var result = await authService.AuthorizeAsync(http.User, policy);
            if (result.Succeeded)
                return null;
        }
        else if (authenticated)
        {
            // Default authorization with no named policy → an authenticated user is sufficient.
            return null;
        }

        return authenticated ? Results.Forbid() : Results.Challenge();
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
            // 'private': responses are typically tenant- or user-scoped; a shared cache (CDN/
            // reverse proxy) must not serve one principal's payload to another.
            res.Headers.CacheControl = $"private, max-age={opt.CacheControlMaxAgeSeconds}";
        }

        return Results.Ok(result);
    }

    private static async Task<IResult> HandleHead(ResourceContract c, HttpRequest req, HttpResponse res, IServiceProvider sp, DataSurfaceHttpOptions opt, CancellationToken ct)
    {
        var crud = sp.GetRequiredService<IDataSurfaceCrudService>();

        // Use minimal page size since we only need the count
        var spec = DataSurfaceQueryParser.ParseQuerySpec(req, c);
        // Report the page size an equivalent GET would use, not the internal count-only size.
        var effectivePageSize = Math.Clamp(spec.PageSize, 1, c.Query.MaxPageSize);
        spec = spec with { PageSize = 1 };

        var result = await crud.ListAsync(c.ResourceKey, spec, expand: null, ct);

        res.Headers["X-Total-Count"] = result.Total.ToString();
        res.Headers["X-Page"] = result.Page.ToString();
        res.Headers["X-Page-Size"] = effectivePageSize.ToString();

        // Set Cache-Control header if configured
        if (opt.CacheControlMaxAgeSeconds > 0)
        {
            // 'private': responses are typically tenant- or user-scoped; a shared cache (CDN/
            // reverse proxy) must not serve one principal's payload to another.
            res.Headers.CacheControl = $"private, max-age={opt.CacheControlMaxAgeSeconds}";
        }

        return Results.StatusCode(200);
    }

    private static async Task<IResult> HandleGet(ResourceContract c, string id, HttpRequest req, HttpResponse res, IServiceProvider sp, DataSurfaceHttpOptions opt, CancellationToken ct)
    {
        var crud = sp.GetRequiredService<IDataSurfaceCrudService>();

        var expand = DataSurfaceQueryParser.ParseExpand(req, c);
        var keyObj = ParseId(id, c);

        var obj = await crud.GetAsync(c.ResourceKey, keyObj, expand, ct);
        if (obj is null) return Results.NotFound();

        // Apply field projection if ?fields= is specified (kill-switched by EnableFieldProjection)
        if ((sp.GetService<DataSurfaceFeatures>()?.EnableFieldProjection ?? true)
            && req.Query.TryGetValue("fields", out var fieldsParam) && !string.IsNullOrWhiteSpace(fieldsParam))
        {
            var requested = new HashSet<string>(
                fieldsParam.ToString().Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
                StringComparer.OrdinalIgnoreCase);
            var keysToRemove = obj.Select(kv => kv.Key).Where(k => !requested.Contains(k)).ToList();
            foreach (var key in keysToRemove)
                obj.Remove(key);
        }

        // Set ETag and check for conditional GET (304 Not Modified)
        var etag = DataSurfaceHttpEtags.TrySetEtag(res, c, obj, opt.EnableEtags);
        if (opt.EnableConditionalGet && etag is not null
            && DataSurfaceHttpEtags.IfNoneMatchMatches(req.Headers.IfNoneMatch, etag))
        {
            return Results.StatusCode(304);
        }

        // Set Cache-Control header if configured
        if (opt.CacheControlMaxAgeSeconds > 0)
        {
            // 'private': responses are typically tenant- or user-scoped; a shared cache (CDN/
            // reverse proxy) must not serve one principal's payload to another.
            res.Headers.CacheControl = $"private, max-age={opt.CacheControlMaxAgeSeconds}";
        }

        return Results.Ok(obj);
    }

    private static async Task<IResult> HandleCreate(ResourceContract c, JsonObject body, HttpRequest req, HttpResponse res, IServiceProvider sp, DataSurfaceHttpOptions opt, CancellationToken ct)
    {
        var crud = sp.GetRequiredService<IDataSurfaceCrudService>();

        var created = await crud.CreateAsync(c.ResourceKey, body, ct);

        DataSurfaceHttpEtags.TrySetEtag(res, c, created, opt.EnableEtags);

        // Location: relative URI (the inbound Host header is client-controlled and PathBase can
        // be stripped by proxies), with the id URL-encoded for string keys.
        var keyApi = GetKeyApiName(c);
        if (created.TryGetPropertyValue(keyApi, out var idNode) && idNode != null)
        {
            var idVal = idNode.ToJsonString().Trim('"');
            return Results.Created($"{req.PathBase}{req.Path}/{Uri.EscapeDataString(idVal)}", created);
        }

        return Results.Created($"{req.PathBase}{req.Path}", created);
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
        // A bulk request executes creates, updates AND deletes; each non-empty section must
        // satisfy that operation's policy — a caller with only the Create policy must not be
        // able to mass-update or mass-delete through /bulk.
        if (spec.Create.Count > 0)
        {
            var deny = await EnforceDynamicPolicyAsync(c, CrudOperation.Create, req.HttpContext, opt);
            if (deny is not null) return deny;
        }
        if (spec.Update.Count > 0)
        {
            var deny = await EnforceDynamicPolicyAsync(c, CrudOperation.Update, req.HttpContext, opt);
            if (deny is not null) return deny;
        }
        if (spec.Delete.Count > 0)
        {
            var deny = await EnforceDynamicPolicyAsync(c, CrudOperation.Delete, req.HttpContext, opt);
            if (deny is not null) return deny;
        }

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

        return Results.Stream(async stream =>
        {
            await using var writer = new System.IO.StreamWriter(stream);
            await foreach (var item in streaming.StreamAsync(c.ResourceKey, spec, expand, ct))
            {
                await writer.WriteLineAsync(item.ToJsonString());
                await writer.FlushAsync(ct);
            }
        }, contentType: "application/x-ndjson");
    }

    private static async Task<IResult> HandlePut(ResourceContract c, string id, JsonObject body, HttpRequest req, HttpResponse res, IServiceProvider sp, DataSurfaceHttpOptions opt, CancellationToken ct)
    {
        var crud = sp.GetRequiredService<IDataSurfaceCrudService>();

        // If-Match -> concurrency token (RowVersion)
        DataSurfaceHttpEtags.ApplyIfMatchToPatch(c, req, body, opt.EnableEtags);

        var keyObj = ParseId(id, c);

        // For PUT (full replacement), validate that all updatable fields are present.
        // Body keys are matched case-insensitively, like the rest of the pipeline.
        var oc = c.Operations[CrudOperation.Update];
        var bodyKeys = new HashSet<string>(body.Select(kv => kv.Key), StringComparer.OrdinalIgnoreCase);
        var missing = oc.InputShape
            .Where(fieldName => !bodyKeys.Contains(fieldName))
            .ToList();

        if (missing.Count > 0)
        {
            var errors = missing.ToDictionary(
                f => f, 
                _ => new[] { "Field is required for PUT (full replacement)." });
            return Results.ValidationProblem(errors, title: "PUT requires all updatable fields");
        }

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

        // Export all records by paginating through the entire dataset
        var spec = DataSurfaceQueryParser.ParseQuerySpec(req, c);
        var allItems = new List<System.Text.Json.Nodes.JsonObject>();
        var page = 1;
        int total;
        var truncated = false;
        do
        {
            var batchSpec = spec with { Page = page, PageSize = c.Query.MaxPageSize };
            var result = await crud.ListAsync(c.ResourceKey, batchSpec, expand: null, ct);

            // An empty page means pagination cannot reach the reported total (e.g. an override
            // returning an inconsistent Total) — bail out instead of spinning forever.
            if (result.Items.Count == 0)
                break;

            allItems.AddRange(result.Items);
            total = result.Total;
            page++;

            // Bound memory: export materializes the whole result set, so cap it.
            if (allItems.Count >= opt.MaxExportRows)
            {
                if (allItems.Count > opt.MaxExportRows)
                    allItems.RemoveRange(opt.MaxExportRows, allItems.Count - opt.MaxExportRows);
                truncated = allItems.Count < total;
                break;
            }
        } while (allItems.Count < total);

        if (truncated)
            res.Headers["X-Export-Truncated"] = "true";

        if (format == "csv")
        {
            res.Headers.ContentDisposition = $"attachment; filename=\"{c.ResourceKey}_export.csv\"";

            var fields = c.Fields.Where(field => field.InRead && !field.Hidden).ToList();
            var csv = new System.Text.StringBuilder();

            // Header row
            csv.AppendLine(string.Join(",", fields.Select(field => $"\"{field.ApiName}\"")));

            // Data rows
            foreach (var item in allItems)
            {
                var values = fields.Select(field =>
                {
                    if (item.TryGetPropertyValue(field.ApiName, out var val) && val != null)
                    {
                        var str = val.ToString();

                        // CSV/formula injection: cells starting with =, +, -, @, tab or CR are
                        // interpreted as formulas by Excel/LibreOffice. Neutralize with a
                        // leading apostrophe.
                        if (str.Length > 0 && str[0] is '=' or '+' or '-' or '@' or '\t' or '\r')
                            str = "'" + str;

                        str = str.Replace("\"", "\"\"");
                        return $"\"{str}\"";
                    }
                    return "\"\"";
                });
                csv.AppendLine(string.Join(",", values));
            }

            return Results.Text(csv.ToString(), "text/csv");
        }

        res.Headers.ContentDisposition = $"attachment; filename=\"{c.ResourceKey}_export.json\"";
        return Results.Ok(allItems);
    }

    private static async Task<IResult> HandleImport(ResourceContract c, HttpRequest req, HttpResponse res, IServiceProvider sp, DataSurfaceHttpOptions opt, CancellationToken ct)
    {
        var crud = sp.GetRequiredService<IDataSurfaceCrudService>();

        // Read request body as JSON array
        using var reader = new System.IO.StreamReader(req.Body);
        var bodyText = await reader.ReadToEndAsync(ct);

        JsonArray? items;
        try
        {
            items = System.Text.Json.JsonSerializer.Deserialize<JsonArray>(bodyText);
        }
        catch (System.Text.Json.JsonException)
        {
            return Results.BadRequest(new { error = "Request body must be a valid JSON array." });
        }
        if (items == null || items.Count == 0)
            return Results.BadRequest(new { error = "Request body must be a non-empty JSON array." });

        var successCount = 0;
        var failureCount = 0;
        var errors = new List<object>();

        // Wrap import in a transaction for atomicity
        var db = sp.GetRequiredService<Microsoft.EntityFrameworkCore.DbContext>();
        await using var transaction = await db.Database.BeginTransactionAsync(ct);

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
                // Only DataSurface's own client-facing exceptions carry safe messages; anything
                // else (DbUpdateException, provider errors) would leak internals to the caller.
                var message = ex switch
                {
                    CrudRequestValidationException vex =>
                        string.Join(" ", vex.Errors.Select(e => $"{e.Key}: {string.Join(", ", e.Value)}")),
                    CrudNotFoundException or CrudConcurrencyException or CrudOperationDisabledException => ex.Message,
                    _ => "Failed to import row."
                };
                errors.Add(new { row = rowNum, error = message });
            }
        }

        // Commit only if all records succeeded; rollback on any failure
        if (failureCount == 0)
            await transaction.CommitAsync(ct);
        else
            await transaction.RollbackAsync(ct);

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
        // TryParse: int/long.Parse throw OverflowException for out-of-range input, which the
        // error mapper does not treat as a 400. FormatException maps to 400 consistently.
        return c.Key.Type switch
        {
            FieldType.Int32 => int.TryParse(raw, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var i)
                ? i
                : throw new FormatException($"'{raw}' is not a valid integer id."),
            FieldType.Int64 => long.TryParse(raw, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var l)
                ? l
                : throw new FormatException($"'{raw}' is not a valid integer id."),
            FieldType.Guid => Guid.TryParse(raw, out var g)
                ? g
                : throw new FormatException($"'{raw}' is not a valid GUID id."),
            FieldType.String => raw,
            _ => raw
        };
    }

    private static string GetKeyApiName(ResourceContract c)
    {
        var keyField = c.Fields.FirstOrDefault(f => f.Name.Equals(c.Key.Name, StringComparison.OrdinalIgnoreCase));
        return keyField?.ApiName ?? c.Key.Name;
    }
}
