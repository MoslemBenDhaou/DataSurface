namespace DataSurface.Admin;

/// <summary>
/// Options controlling DataSurface administration endpoints and storage.
/// </summary>
public sealed class DataSurfaceAdminOptions
{
    /// <summary>
    /// Gets or sets the route prefix used to map administration endpoints.
    /// </summary>
    public string Prefix { get; set; } = "/admin/ds";

    /// <summary>
    /// Gets or sets the database schema used for dynamic metadata tables.
    /// </summary>
    public string Schema { get; set; } = "dbo";

    // strongly recommended
    /// <summary>
    /// Gets or sets whether the admin endpoints require authorization.
    /// </summary>
    public bool RequireAuthorization { get; set; } = true;

    /// <summary>
    /// Gets or sets the authorization policy name used when <see cref="RequireAuthorization"/> is enabled.
    /// If null, uses the default authorization policy.
    /// </summary>
    public string? Policy { get; set; } = null;
}
