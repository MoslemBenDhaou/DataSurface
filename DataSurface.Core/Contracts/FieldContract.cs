using DataSurface.Core.Enums;

namespace DataSurface.Core.Contracts;

/// <summary>
/// Describes a scalar field exposed by a resource.
/// </summary>
/// <param name="Name">Canonical CLR property name.</param>
/// <param name="ApiName">External API name (typically camelCase).</param>
/// <param name="Type">Canonical contract field type.</param>
/// <param name="Nullable">Whether the field is nullable.</param>
/// <param name="InRead">Whether the field is included in read output.</param>
/// <param name="InCreate">Whether the field is accepted on create.</param>
/// <param name="InUpdate">Whether the field is accepted on update.</param>
/// <param name="Filterable">Whether the field may be used for filtering.</param>
/// <param name="Sortable">Whether the field may be used for sorting.</param>
/// <param name="Hidden">Whether the field is hard-hidden (never accepted/emitted).</param>
/// <param name="Immutable">Whether the field is immutable (cannot be changed on update).</param>
/// <param name="Searchable">Whether the field is included in full-text search.</param>
/// <param name="Computed">Whether the field is computed (read-only, server-calculated).</param>
/// <param name="ComputedExpression">Expression for computed fields (e.g., "FirstName + ' ' + LastName").</param>
/// <param name="DefaultValue">Default value applied on create when not provided.</param>
/// <param name="Validation">Validation rules for the field.</param>
public sealed record FieldContract(
    string Name,
    string ApiName,
    FieldType Type,
    bool Nullable,
    bool InRead,
    bool InCreate,
    bool InUpdate,
    bool Filterable,
    bool Sortable,
    bool Hidden,
    bool Immutable,
    bool Searchable,
    bool Computed,
    string? ComputedExpression,
    object? DefaultValue,
    FieldValidationContract Validation
);