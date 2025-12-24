namespace DataSurface.Http;

/// <summary>
/// Options controlling how DataSurface HTTP endpoints are mapped.
/// </summary>
public sealed class DataSurfaceHttpOptions
{
    /// <summary>
    /// Gets or sets the API route prefix used when mapping DataSurface endpoints.
    /// </summary>
    public string ApiPrefix { get; set; } = "/api";

    // Static resources mapped at startup from provider.All
    /// <summary>
    /// Gets or sets whether to map static resources discovered from the contract provider at startup.
    /// </summary>
    public bool MapStaticResources { get; set; } = true;

    // Dynamic resources mapped via catch-all routes: /api/d/{route}
    /// <summary>
    /// Gets or sets whether to map dynamic resources through catch-all routes.
    /// </summary>
    public bool MapDynamicCatchAll { get; set; } = true;

    // Prefix for dynamic routes
    /// <summary>
    /// Gets or sets the route prefix for dynamic resources (relative to <see cref="ApiPrefix"/>).
    /// </summary>
    public string DynamicPrefix { get; set; } = "/d";

    // Discovery endpoint
    /// <summary>
    /// Gets or sets whether to map the resource discovery endpoint.
    /// </summary>
    public bool MapResourceDiscoveryEndpoint { get; set; } = true;

    // Security
    /// <summary>
    /// Gets or sets whether endpoints require authorization by default.
    /// </summary>
    public bool RequireAuthorizationByDefault { get; set; } = false;

    /// <summary>
    /// Gets or sets the default authorization policy name applied when
    /// <see cref="RequireAuthorizationByDefault"/> is enabled.
    /// </summary>
    public string? DefaultPolicy { get; set; } = null;

    // ETag/If-Match
    /// <summary>
    /// Gets or sets whether to enable ETag and If-Match handling for row-version concurrency.
    /// </summary>
    public bool EnableEtags { get; set; } = true;

    // Strict: if true, fail fast on route collisions
    /// <summary>
    /// Gets or sets whether to throw an exception when a route collision is detected during mapping.
    /// </summary>
    public bool ThrowOnRouteCollision { get; set; } = false;

    // Bulk operations
    /// <summary>
    /// Gets or sets whether to map bulk operation endpoints (POST /api/{resource}/bulk).
    /// </summary>
    public bool EnableBulkOperations { get; set; } = true;

    // Streaming
    /// <summary>
    /// Gets or sets whether to map streaming endpoints (GET /api/{resource}/stream).
    /// </summary>
    public bool EnableStreaming { get; set; } = true;

    // Response caching
    /// <summary>
    /// Gets or sets the default Cache-Control max-age in seconds for GET responses.
    /// Set to 0 to disable response caching headers.
    /// </summary>
    public int CacheControlMaxAgeSeconds { get; set; } = 0;

    /// <summary>
    /// Gets or sets whether to support If-None-Match headers for 304 responses on GET requests.
    /// </summary>
    public bool EnableConditionalGet { get; set; } = true;

    /// <summary>
    /// Gets or sets whether to enable PUT endpoints for full replacement updates (alongside PATCH).
    /// </summary>
    public bool EnablePutForFullUpdate { get; set; } = false;

    /// <summary>
    /// Gets or sets whether to enable import/export endpoints (POST/GET /api/{resource}/import, /export).
    /// </summary>
    public bool EnableImportExport { get; set; } = false;

    /// <summary>
    /// Gets or sets whether to enable rate limiting per resource/operation.
    /// Requires ASP.NET Core rate limiting middleware to be configured.
    /// </summary>
    public bool EnableRateLimiting { get; set; } = false;

    /// <summary>
    /// Gets or sets the default rate limiting policy name applied to endpoints.
    /// </summary>
    public string? RateLimitingPolicy { get; set; }

    /// <summary>
    /// Gets or sets whether to enable API key authentication for CRUD endpoints.
    /// </summary>
    public bool EnableApiKeyAuth { get; set; } = false;

    /// <summary>
    /// Gets or sets the header name for API key authentication.
    /// </summary>
    public string ApiKeyHeaderName { get; set; } = "X-Api-Key";

    /// <summary>
    /// Gets or sets whether to enable webhook publishing for CRUD events.
    /// </summary>
    public bool EnableWebhooks { get; set; } = false;
}
