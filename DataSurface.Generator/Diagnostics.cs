using Microsoft.CodeAnalysis;

namespace DataSurface.Generator;

/// <summary>
/// Diagnostic descriptors emitted by the DataSurface source generator.
/// </summary>
internal static class Diagnostics
{
    private const string Category = "DataSurface.Generator";

    /// <summary>
    /// DSG001: the [CrudResource] route is missing or empty.
    /// </summary>
    public static readonly DiagnosticDescriptor MissingRoute =
        new("DSG001", "Missing route", "The [CrudResource] route on '{0}' is missing or empty", Category,
            DiagnosticSeverity.Error, isEnabledByDefault: true);

    /// <summary>
    /// DSG002: multiple fields resolve to the same API name.
    /// </summary>
    public static readonly DiagnosticDescriptor DuplicateApiName =
        new("DSG002", "Duplicate ApiName", "Duplicate API name '{0}' in resource '{1}'", Category,
            DiagnosticSeverity.Error, isEnabledByDefault: true);

    /// <summary>
    /// DSG003: no key property could be identified for a resource.
    /// </summary>
    public static readonly DiagnosticDescriptor MissingKey =
        new("DSG003", "Missing key", "No key property found for resource '{0}'. Add [CrudKey], an 'Id' or '{1}Id' property, or set CrudResourceAttribute.KeyProperty.", Category,
            DiagnosticSeverity.Error, isEnabledByDefault: true);

    /// <summary>
    /// DSG004: multiple [CrudKey] properties were found for a resource.
    /// </summary>
    public static readonly DiagnosticDescriptor MultipleKeys =
        new("DSG004", "Multiple keys", "Multiple [CrudKey] properties found for resource '{0}'; only one is allowed", Category,
            DiagnosticSeverity.Error, isEnabledByDefault: true);

    /// <summary>
    /// DSG005: a property is marked [CrudIgnore] but also has [CrudField].
    /// </summary>
    public static readonly DiagnosticDescriptor IgnoreFieldConflict =
        new("DSG005", "Conflicting CrudIgnore and CrudField", "Property '{0}' in resource '{1}' has both [CrudIgnore] and [CrudField]; remove one", Category,
            DiagnosticSeverity.Error, isEnabledByDefault: true);

    /// <summary>
    /// DSG006: generic entity types are not supported.
    /// </summary>
    public static readonly DiagnosticDescriptor GenericResourceNotSupported =
        new("DSG006", "Generic resource not supported", "Resource type '{0}' is generic; generic entity types are not supported by the DataSurface generator", Category,
            DiagnosticSeverity.Error, isEnabledByDefault: true);

    /// <summary>
    /// DSG007: an ApiName does not produce a valid C# identifier.
    /// </summary>
    public static readonly DiagnosticDescriptor InvalidApiName =
        new("DSG007", "Invalid ApiName", "API name '{0}' on property '{1}' in resource '{2}' does not produce a valid C# identifier", Category,
            DiagnosticSeverity.Error, isEnabledByDefault: true);

    /// <summary>
    /// DSG008: the CrudResourceAttribute.KeyProperty override names a property that does not exist.
    /// </summary>
    public static readonly DiagnosticDescriptor KeyPropertyNotFound =
        new("DSG008", "KeyProperty not found", "KeyProperty '{0}' was not found on resource type '{1}'", Category,
            DiagnosticSeverity.Error, isEnabledByDefault: true);

    /// <summary>
    /// DSG009: [CrudField] was applied to a navigation-shaped (non-scalar) property.
    /// </summary>
    public static readonly DiagnosticDescriptor NavigationFieldNotSupported =
        new("DSG009", "CrudField on navigation property", "Property '{0}' in resource '{1}' has [CrudField] but is not a supported scalar type; use [CrudRelation] for navigations or remove the attribute", Category,
            DiagnosticSeverity.Error, isEnabledByDefault: true);
}
