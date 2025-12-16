using DataSurface.Core.Contracts;
using DataSurface.Core.Enums;

namespace DataSurface.Core.Annotations;

 /// <summary>
 /// Marks a CLR type as a CRUD-exposed resource for contract generation.
 /// </summary>
 /// <remarks>
 /// A type annotated with this attribute is discovered by <see cref="ContractBuilder"/> and normalized into a
 /// <see cref="ResourceContract"/>.
 /// </remarks>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false)]
public sealed class CrudResourceAttribute : Attribute
{
    /// <summary>
    /// Creates a new resource marker for the given route.
    /// </summary>
    /// <param name="route">The resource route segment (for example <c>"users"</c>).</param>
    public CrudResourceAttribute(string route) => Route = route;

    /// <summary>
    /// Gets the route segment used when building CRUD endpoints for this resource.
    /// </summary>
    public string Route { get; }
    /// <summary>
    /// Gets or sets the stable resource identifier (defaults to CLR type name).
    /// </summary>
    public string? ResourceKey { get; set; }         // default: CLR type name
    /// <summary>
    /// Gets or sets the backend that will execute CRUD operations for this resource.
    /// </summary>
    public StorageBackend Backend { get; set; } = StorageBackend.EfCore;

    /// <summary>
    /// Gets or sets an explicit key property name (overrides key discovery).
    /// </summary>
    public string? KeyProperty { get; set; }         // override key discovery
    /// <summary>
    /// Gets or sets the maximum page size allowed for list operations.
    /// </summary>
    public int MaxPageSize { get; set; } = 200;

    /// <summary>
    /// Gets or sets whether the List operation is enabled for this resource.
    /// </summary>
    public bool EnableList { get; set; } = true;
    /// <summary>
    /// Gets or sets whether the Get operation is enabled for this resource.
    /// </summary>
    public bool EnableGet { get; set; } = true;
    /// <summary>
    /// Gets or sets whether the Create operation is enabled for this resource.
    /// </summary>
    public bool EnableCreate { get; set; } = true;
    /// <summary>
    /// Gets or sets whether the Update operation is enabled for this resource.
    /// </summary>
    public bool EnableUpdate { get; set; } = true;
    /// <summary>
    /// Gets or sets whether the Delete operation is enabled for this resource.
    /// </summary>
    public bool EnableDelete { get; set; } = true;

    /// <summary>
    /// Gets or sets the maximum depth allowed when expanding relations during reads.
    /// </summary>
    public int MaxExpandDepth { get; set; } = 1;
}