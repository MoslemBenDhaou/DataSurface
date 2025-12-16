using Microsoft.CodeAnalysis;

namespace DataSurface.Generator;

/// <summary>
/// Diagnostic descriptors emitted by the DataSurface source generator.
/// </summary>
internal static class Diagnostics
{
    /// <summary>
    /// Diagnostic reported when a CRUD resource route is missing.
    /// </summary>
    public static readonly DiagnosticDescriptor MissingRoute =
        new("DSG001", "Missing route", "CrudResource route is missing or empty on '{0}'", "DataSurface.Generator",
            DiagnosticSeverity.Error, isEnabledByDefault: true);

    /// <summary>
    /// Diagnostic reported when multiple fields resolve to the same API name.
    /// </summary>
    public static readonly DiagnosticDescriptor DuplicateApiName =
        new("DSG002", "Duplicate ApiName", "Duplicate ApiName '{0}' in resource '{1}'", "DataSurface.Generator",
            DiagnosticSeverity.Error, isEnabledByDefault: true);

    /// <summary>
    /// Diagnostic reported when no key property can be identified for a resource.
    /// </summary>
    public static readonly DiagnosticDescriptor MissingKey =
        new("DSG003", "Missing key", "No key property found for resource '{0}'. Add [CrudKey] or an 'Id' property.", "DataSurface.Generator",
            DiagnosticSeverity.Error, isEnabledByDefault: true);

    /// <summary>
    /// Diagnostic reported when multiple key candidates are found for a resource.
    /// </summary>
    public static readonly DiagnosticDescriptor MultipleKeys =
        new("DSG004", "Multiple keys", "Multiple key properties found for resource '{0}'. Only one is allowed.", "DataSurface.Generator",
            DiagnosticSeverity.Error, isEnabledByDefault: true);

    /// <summary>
    /// Diagnostic reported when a property is marked ignored but also has DTO flags.
    /// </summary>
    public static readonly DiagnosticDescriptor InvalidDtoFlags =
        new("DSG005", "Invalid DTO flags", "Property '{0}' in '{1}' has CrudField flags but is marked [CrudIgnore]", "DataSurface.Generator",
            DiagnosticSeverity.Error, isEnabledByDefault: true);
}
