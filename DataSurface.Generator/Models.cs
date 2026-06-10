using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

namespace DataSurface.Generator;

/// <summary>
/// The result of analyzing one [CrudResource] declaration: an optional resource model plus any
/// diagnostics. Fully value-equatable so the incremental pipeline caches correctly.
/// </summary>
/// <param name="Resource">The resource model, or <c>null</c> when a fatal diagnostic was reported.</param>
/// <param name="Diagnostics">Diagnostics to report for this declaration.</param>
internal sealed record ResourceResult(
    ResourceModel? Resource,
    EquatableArray<DiagnosticInfo> Diagnostics);

/// <summary>
/// Model describing a CRUD resource discovered by the generator. Contains only value-equatable
/// data (strings/bools) — no symbols and no compilation state.
/// </summary>
/// <param name="Namespace">The entity's containing namespace ("" for the global namespace).</param>
/// <param name="IsGlobalNamespace">Whether the entity lives in the global namespace.</param>
/// <param name="EntityName">The entity CLR name.</param>
/// <param name="ResourceKey">The stable resource key.</param>
/// <param name="Route">The route segment from [CrudResource].</param>
/// <param name="HintName">Collision-free hint-name prefix (sanitized namespace + type name).</param>
/// <param name="EnableList">Whether the List endpoint is enabled.</param>
/// <param name="EnableGet">Whether the Get endpoint is enabled.</param>
/// <param name="EnableCreate">Whether the Create endpoint is enabled.</param>
/// <param name="EnableUpdate">Whether the Update endpoint is enabled.</param>
/// <param name="EnableDelete">Whether the Delete endpoint is enabled.</param>
/// <param name="ListPolicy">Authorization policy for List, if any.</param>
/// <param name="GetPolicy">Authorization policy for Get, if any.</param>
/// <param name="CreatePolicy">Authorization policy for Create, if any.</param>
/// <param name="UpdatePolicy">Authorization policy for Update, if any.</param>
/// <param name="DeletePolicy">Authorization policy for Delete, if any.</param>
/// <param name="KeyReadIdentifier">The generated Read-DTO property identifier of the key (null when the key is not readable).</param>
/// <param name="Properties">The flattened, resolved field models.</param>
internal sealed record ResourceModel(
    string Namespace,
    bool IsGlobalNamespace,
    string EntityName,
    string ResourceKey,
    string Route,
    string HintName,
    bool EnableList,
    bool EnableGet,
    bool EnableCreate,
    bool EnableUpdate,
    bool EnableDelete,
    string? ListPolicy,
    string? GetPolicy,
    string? CreatePolicy,
    string? UpdatePolicy,
    string? DeletePolicy,
    string? KeyReadIdentifier,
    EquatableArray<PropertyModel> Properties)
{
    /// <summary>
    /// Gets the namespace the generated DTOs are emitted into.
    /// </summary>
    public string DtoNamespace => IsGlobalNamespace ? "DataSurfaceGenerated" : Namespace + ".DataSurfaceGenerated";
}

/// <summary>
/// Model describing a single scalar property exposed by a resource.
/// </summary>
/// <param name="Name">The CLR property name.</param>
/// <param name="ApiName">The API (wire) field name.</param>
/// <param name="Identifier">The generated C# property identifier (PascalCased ApiName, keyword-escaped).</param>
/// <param name="TypeRef">Fully qualified (global::) type reference including nullable annotations.</param>
/// <param name="TypeRefNullable">Same as <paramref name="TypeRef"/> but guaranteed nullable (for PATCH shapes).</param>
/// <param name="NeedsNullForgivingInit">Whether the property requires a <c>= default!;</c> initializer (non-nullable reference type).</param>
/// <param name="InRead">Whether the property is included in read output.</param>
/// <param name="InCreate">Whether the property is accepted on create.</param>
/// <param name="InUpdate">Whether the property is accepted on update.</param>
/// <param name="RequiredOnCreate">Whether the property is required on create.</param>
internal sealed record PropertyModel(
    string Name,
    string ApiName,
    string Identifier,
    string TypeRef,
    string TypeRefNullable,
    bool NeedsNullForgivingInit,
    bool InRead,
    bool InCreate,
    bool InUpdate,
    bool RequiredOnCreate);

/// <summary>
/// A value-equatable diagnostic captured in the transform stage (a <see cref="Diagnostic"/>
/// itself holds syntax-tree references and must not be cached).
/// </summary>
/// <param name="Descriptor">The diagnostic descriptor (shared static instance).</param>
/// <param name="Position">The location, if available.</param>
/// <param name="Args">Message format arguments.</param>
internal sealed record DiagnosticInfo(
    DiagnosticDescriptor Descriptor,
    LocationInfo? Position,
    EquatableArray<string> Args)
{
    /// <summary>
    /// Creates a new diagnostic info.
    /// </summary>
    /// <param name="descriptor">The diagnostic descriptor.</param>
    /// <param name="location">A Roslyn location (may be null or in metadata).</param>
    /// <param name="args">Message format arguments.</param>
    public static DiagnosticInfo Create(DiagnosticDescriptor descriptor, Location? location, params string[] args)
        => new(descriptor, LocationInfo.CreateFrom(location), new EquatableArray<string>(args));

    /// <summary>
    /// Materializes this info into a reportable <see cref="Diagnostic"/>.
    /// </summary>
    public Diagnostic ToDiagnostic()
        => Diagnostic.Create(
            Descriptor,
            Position?.ToLocation() ?? Location.None,
            // string[] is covariantly an object[]; message args are always strings here.
            Args.UnderlyingArray);
}

/// <summary>
/// A value-equatable snapshot of a source location.
/// </summary>
/// <param name="FilePath">The source file path.</param>
/// <param name="TextSpan">The text span.</param>
/// <param name="LineSpan">The line/column span.</param>
internal readonly record struct LocationInfo(string FilePath, TextSpan TextSpan, LinePositionSpan LineSpan)
{
    /// <summary>
    /// Recreates a Roslyn <see cref="Location"/> from this snapshot.
    /// </summary>
    public Location ToLocation() => Location.Create(FilePath, TextSpan, LineSpan);

    /// <summary>
    /// Captures a snapshot of <paramref name="location"/>, or <c>null</c> when it has no source tree.
    /// </summary>
    /// <param name="location">The location to snapshot.</param>
    public static LocationInfo? CreateFrom(Location? location)
        => location?.SourceTree is null
            ? null
            : new LocationInfo(location.SourceTree.FilePath, location.SourceSpan, location.GetLineSpan().Span);
}
