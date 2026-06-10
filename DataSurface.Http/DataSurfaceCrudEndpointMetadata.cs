using DataSurface.Core.Enums;

namespace DataSurface.Http;

/// <summary>
/// Distinguishes the shape of a mapped DataSurface endpoint so downstream tooling (OpenAPI)
/// can document it correctly. CRUD endpoints exchange resource shapes; the auxiliary kinds
/// have their own request/response formats.
/// </summary>
public enum DataSurfaceEndpointKind
{
    /// <summary>A standard CRUD endpoint (list/get/create/update/delete/put).</summary>
    Crud,
    /// <summary>The bulk endpoint (<c>POST {route}/bulk</c>, BulkOperationSpec body).</summary>
    Bulk,
    /// <summary>The import endpoint (<c>POST {route}/import</c>, JSON array body).</summary>
    Import,
    /// <summary>The export endpoint (<c>GET {route}/export</c>, JSON/CSV download).</summary>
    Export,
    /// <summary>The streaming endpoint (<c>GET {route}/stream</c>, NDJSON).</summary>
    Stream,
    /// <summary>The HEAD count endpoint.</summary>
    Head
}

/// <summary>
/// Metadata attached to mapped DataSurface CRUD endpoints.
/// </summary>
/// <param name="ResourceKey">The logical resource key the endpoint operates on.</param>
/// <param name="Operation">The CRUD operation represented by the endpoint.</param>
/// <param name="Kind">The endpoint kind; defaults to <see cref="DataSurfaceEndpointKind.Crud"/>.</param>
public sealed record DataSurfaceCrudEndpointMetadata(
    string ResourceKey,
    CrudOperation Operation,
    DataSurfaceEndpointKind Kind = DataSurfaceEndpointKind.Crud);
