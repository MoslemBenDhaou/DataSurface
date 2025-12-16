using DataSurface.Core.Enums;

namespace DataSurface.Admin.Dtos;

/// <summary>
/// DTO representing a dynamic entity definition managed through the admin API.
/// </summary>
public sealed class AdminEntityDefDto
{
    /// <summary>
    /// Gets or sets the database identifier for the entity definition.
    /// </summary>
    public int? Id { get; set; }

    /// <summary>
    /// Gets or sets the logical entity key (unique identifier) used by clients.
    /// </summary>
    public string EntityKey { get; set; } = default!;

    /// <summary>
    /// Gets or sets the route segment used to address the resource over HTTP.
    /// </summary>
    public string Route { get; set; } = default!;

    /// <summary>
    /// Gets or sets the backing storage type for the resource.
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
    /// Gets or sets the maximum allowed page size for list operations.
    /// </summary>
    public int MaxPageSize { get; set; } = 200;

    /// <summary>
    /// Gets or sets the maximum allowed depth when expanding relations.
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
    /// Gets or sets the property definitions that make up the resource schema.
    /// </summary>
    public List<AdminPropertyDefDto> Properties { get; set; } = new();

    /// <summary>
    /// Gets or sets the relation definitions exposed by the resource.
    /// </summary>
    public List<AdminRelationDefDto> Relations { get; set; } = new();
}
