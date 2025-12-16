namespace DataSurface.Core.Enums;

/// <summary>
/// Flags indicating which DTO shapes and query capabilities a field participates in.
/// </summary>
[Flags]
public enum CrudDto
{
    /// <summary>
    /// The field is not included in any shape.
    /// </summary>
    None   = 0,
    /// <summary>
    /// The field is included in read responses.
    /// </summary>
    Read   = 1 << 0,
    /// <summary>
    /// The field may be provided during create operations.
    /// </summary>
    Create = 1 << 1,
    /// <summary>
    /// The field may be provided during update operations.
    /// </summary>
    Update = 1 << 2,
    /// <summary>
    /// The field may be used in filtering expressions.
    /// </summary>
    Filter = 1 << 3,
    /// <summary>
    /// The field may be used in sorting expressions.
    /// </summary>
    Sort   = 1 << 4
}

/// <summary>
/// CRUD operations supported by a resource.
/// </summary>
public enum CrudOperation
{
    /// <summary>
    /// Lists resources.
    /// </summary>
    List,
    /// <summary>
    /// Gets a single resource by its key.
    /// </summary>
    Get,
    /// <summary>
    /// Creates a new resource.
    /// </summary>
    Create,
    /// <summary>
    /// Updates an existing resource.
    /// </summary>
    Update,
    /// <summary>
    /// Deletes an existing resource.
    /// </summary>
    Delete
}

/// <summary>
/// Identifies the backend responsible for storage and execution of CRUD operations for a resource.
/// </summary>
public enum StorageBackend
{
    /// <summary>
    /// Entity Framework Core backed resources.
    /// </summary>
    EfCore,
    /// <summary>
    /// Dynamic resources stored as JSON.
    /// </summary>
    DynamicJson,
    /// <summary>
    /// Dynamic resources stored using an entity-attribute-value model.
    /// </summary>
    DynamicEav,
    /// <summary>
    /// Hybrid dynamic storage.
    /// </summary>
    DynamicHybrid
}

/// <summary>
/// Canonical field types used by the resource contract.
/// </summary>
public enum FieldType
{
    /// <summary>
    /// String value.
    /// </summary>
    String,
    /// <summary>
    /// 32-bit integer value.
    /// </summary>
    Int32,
    /// <summary>
    /// 64-bit integer value.
    /// </summary>
    Int64,
    /// <summary>
    /// Decimal value.
    /// </summary>
    Decimal,
    /// <summary>
    /// Boolean value.
    /// </summary>
    Boolean,
    /// <summary>
    /// Date/time value.
    /// </summary>
    DateTime,
    /// <summary>
    /// GUID value.
    /// </summary>
    Guid,
    /// <summary>
    /// JSON value.
    /// </summary>
    Json,
    /// <summary>
    /// Enumeration value.
    /// </summary>
    Enum,
    /// <summary>
    /// Array of strings.
    /// </summary>
    StringArray,
    /// <summary>
    /// Array of integers.
    /// </summary>
    IntArray,
    /// <summary>
    /// Array of GUIDs.
    /// </summary>
    GuidArray,
    /// <summary>
    /// Array of decimals.
    /// </summary>
    DecimalArray
}

/// <summary>
/// Identifies the kind (cardinality) of a relationship between resources.
/// </summary>
public enum RelationKind
{
    /// <summary>
    /// Many source records relate to one target record.
    /// </summary>
    ManyToOne,
    /// <summary>
    /// One source record relates to many target records.
    /// </summary>
    OneToMany,
    /// <summary>
    /// Many source records relate to many target records.
    /// </summary>
    ManyToMany,
    /// <summary>
    /// One source record relates to one target record.
    /// </summary>
    OneToOne
}

/// <summary>
/// Describes how relation writes are performed.
/// </summary>
public enum RelationWriteMode
{
    /// <summary>
    /// No write support.
    /// </summary>
    None,
    /// <summary>
    /// Writes are performed by specifying a single related ID.
    /// </summary>
    ById,
    /// <summary>
    /// Writes are performed by specifying a list of related IDs.
    /// </summary>
    ByIdList,
    /// <summary>
    /// Nested writes are disabled.
    /// </summary>
    NestedDisabled
}

/// <summary>
/// Concurrency control mechanism used for updates.
/// </summary>
public enum ConcurrencyMode
{
    /// <summary>
    /// No concurrency control.
    /// </summary>
    None,
    /// <summary>
    /// Row-version (byte[]) style concurrency token.
    /// </summary>
    RowVersion,
    /// <summary>
    /// HTTP ETag-based concurrency token.
    /// </summary>
    ETag
}
