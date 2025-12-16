namespace DataSurface.Dynamic.DI;

/// <summary>
/// Options for configuring DataSurface dynamic metadata and storage.
/// </summary>
public sealed class DataSurfaceDynamicOptions
{
    /// <summary>
    /// Gets or sets the database schema used for dynamic metadata tables.
    /// </summary>
    public string Schema { get; set; } = "dbo";

    /// <summary>
    /// Gets or sets whether dynamic contracts should be loaded and cached during application startup.
    /// </summary>
    public bool WarmUpContractsOnStart { get; set; } = true;
}
