using DataSurface.Core.Enums;

namespace DataSurface.Dynamic.Entities;

/// <summary>
/// Database row representing a dynamic entity definition.
/// </summary>
public sealed class DsEntityDefRow
{
    /// <summary>
    /// Gets or sets the database identifier.
    /// </summary>
    public int Id { get; set; }

    /// <summary>
    /// Gets or sets the stable entity key used by clients.
    /// </summary>
    public string EntityKey { get; set; } = default!;
    /// <summary>
    /// Gets or sets the route segment used for addressing the resource.
    /// </summary>
    public string Route { get; set; } = default!;
    /// <summary>
    /// Gets or sets the backing storage type.
    /// </summary>
    public StorageBackend Backend { get; set; } = StorageBackend.DynamicJson;

    /// <summary>
    /// Gets or sets the API name of the key field.
    /// </summary>
    public string KeyName { get; set; } = "id";
    /// <summary>
    /// Gets or sets the type of the key field.
    /// </summary>
    public FieldType KeyType { get; set; } = FieldType.Guid;

    /// <summary>
    /// Gets or sets the maximum allowed page size.
    /// </summary>
    public int MaxPageSize { get; set; } = 200;
    /// <summary>
    /// Gets or sets the maximum allowed expand depth.
    /// </summary>
    public int MaxExpandDepth { get; set; } = 1;

    /// <summary>
    /// Gets or sets whether list operations are enabled.
    /// </summary>
    public bool EnableList { get; set; } = true;
    /// <summary>
    /// Gets or sets whether get-by-id operations are enabled.
    /// </summary>
    public bool EnableGet { get; set; } = true;
    /// <summary>
    /// Gets or sets whether create operations are enabled.
    /// </summary>
    public bool EnableCreate { get; set; } = true;
    /// <summary>
    /// Gets or sets whether update operations are enabled.
    /// </summary>
    public bool EnableUpdate { get; set; } = true;
    /// <summary>
    /// Gets or sets whether delete operations are enabled.
    /// </summary>
    public bool EnableDelete { get; set; } = true;

    /// <summary>
    /// Gets or sets the UTC timestamp when this definition was last updated.
    /// </summary>
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Gets or sets the property definitions for this entity.
    /// </summary>
    public ICollection<DsPropertyDefRow> Properties { get; set; } = new List<DsPropertyDefRow>();
    /// <summary>
    /// Gets or sets the relation definitions for this entity.
    /// </summary>
    public ICollection<DsRelationDefRow> Relations { get; set; } = new List<DsRelationDefRow>();
}
