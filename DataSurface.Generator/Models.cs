using System.Collections.Generic;
using Microsoft.CodeAnalysis;

namespace DataSurface.Generator;

/// <summary>
/// Model describing a CRUD resource discovered by the generator.
/// </summary>
/// <param name="EntitySymbol">The entity type symbol.</param>
/// <param name="Namespace">The target namespace for generated output.</param>
/// <param name="EntityName">The entity CLR name.</param>
/// <param name="ResourceKey">The resource key.</param>
/// <param name="Route">The route segment.</param>
/// <param name="Key">The key property model.</param>
/// <param name="Concurrency">The concurrency property model, if configured.</param>
/// <param name="Fields">The field property models.</param>
internal sealed record ResourceModel(
    INamedTypeSymbol EntitySymbol,
    string Namespace,
    string EntityName,
    string ResourceKey,
    string Route,
    PropertyModel Key,
    PropertyModel? Concurrency,
    IReadOnlyList<PropertyModel> Fields);

/// <summary>
/// Model describing a single property exposed by a resource.
/// </summary>
/// <param name="Name">The CLR property name.</param>
/// <param name="ApiName">The API field name.</param>
/// <param name="Type">The property type symbol.</param>
/// <param name="InRead">Whether the property is included in read output.</param>
/// <param name="InCreate">Whether the property is accepted on create.</param>
/// <param name="InUpdate">Whether the property is accepted on update.</param>
/// <param name="RequiredOnCreate">Whether the property is required on create.</param>
/// <param name="Immutable">Whether the property is immutable.</param>
/// <param name="Hidden">Whether the property is hidden.</param>
/// <param name="ConcurrencyToken">Whether the property is a concurrency token.</param>
internal sealed record PropertyModel(
    string Name,
    string ApiName,
    ITypeSymbol Type,
    bool InRead,
    bool InCreate,
    bool InUpdate,
    bool RequiredOnCreate,
    bool Immutable,
    bool Hidden,
    bool ConcurrencyToken);
