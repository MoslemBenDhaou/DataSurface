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
}
