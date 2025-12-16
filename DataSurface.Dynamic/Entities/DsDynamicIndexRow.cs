namespace DataSurface.Dynamic.Entities;

/// <summary>
/// Database row representing an indexed value for a dynamic record, used to support filtering and sorting.
/// </summary>
public sealed class DsDynamicIndexRow
{
    /// <summary>
    /// Gets or sets the surrogate identifier for the index row.
    /// </summary>
    public long Id { get; set; }

    /// <summary>
    /// Gets or sets the entity key this index row belongs to.
    /// </summary>
    public string EntityKey { get; set; } = default!;
    /// <summary>
    /// Gets or sets the record identifier this index row belongs to.
    /// </summary>
    public string RecordId { get; set; } = default!;
    /// <summary>
    /// Gets or sets the API name of the property being indexed.
    /// </summary>
    public string PropertyApiName { get; set; } = default!;

    /// <summary>
    /// Gets or sets the string value for the index (when applicable).
    /// </summary>
    public string? ValueString { get; set; }
    /// <summary>
    /// Gets or sets the numeric value for the index (when applicable).
    /// </summary>
    public decimal? ValueNumber { get; set; }
    /// <summary>
    /// Gets or sets the date/time value for the index (when applicable).
    /// </summary>
    public DateTime? ValueDateTime { get; set; }
    /// <summary>
    /// Gets or sets the boolean value for the index (when applicable).
    /// </summary>
    public bool? ValueBool { get; set; }
    /// <summary>
    /// Gets or sets the GUID value for the index (when applicable).
    /// </summary>
    public Guid? ValueGuid { get; set; }
}
