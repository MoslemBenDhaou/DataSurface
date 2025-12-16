namespace DataSurface.Dynamic.Entities;

/// <summary>
/// Database row representing a dynamic record stored as JSON.
/// </summary>
public sealed class DsDynamicRecordRow
{
    // string key supports Guid/int/string keys in one schema
    /// <summary>
    /// Gets or sets the record identifier, stored as a string.
    /// </summary>
    public string Id { get; set; } = default!;
    /// <summary>
    /// Gets or sets the owning entity key for this record.
    /// </summary>
    public string EntityKey { get; set; } = default!;

    /// <summary>
    /// Gets or sets the stored JSON payload for the record.
    /// </summary>
    public string DataJson { get; set; } = "{}";

    /// <summary>
    /// Gets or sets whether the record is soft-deleted.
    /// </summary>
    public bool IsDeleted { get; set; }

    /// <summary>
    /// Gets or sets the UTC timestamp when the record was created.
    /// </summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    /// <summary>
    /// Gets or sets the UTC timestamp when the record was last updated.
    /// </summary>
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Gets or sets the row-version concurrency token.
    /// </summary>
    public byte[] RowVersion { get; set; } = default!;
}
