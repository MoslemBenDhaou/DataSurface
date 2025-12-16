using DataSurface.Core.Enums;

namespace DataSurface.Http;

/// <summary>
/// Metadata attached to mapped DataSurface CRUD endpoints.
/// </summary>
/// <param name="ResourceKey">The logical resource key the endpoint operates on.</param>
/// <param name="Operation">The CRUD operation represented by the endpoint.</param>
public sealed record DataSurfaceCrudEndpointMetadata(string ResourceKey, CrudOperation Operation);
