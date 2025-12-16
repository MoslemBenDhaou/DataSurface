using DataSurface.Core.Enums;

namespace DataSurface.Core.Contracts;

/// <summary>
/// Runtime (dynamic) field definition used to build a <see cref="FieldContract"/>.
/// </summary>
public sealed record PropertyDef(
    string Name,
    string ApiName,
    FieldType Type,
    bool Nullable,
    CrudDto In,
    bool RequiredOnCreate = false,
    bool Immutable = false,
    bool Hidden = false,
    int? MinLength = null,
    int? MaxLength = null,
    decimal? Min = null,
    decimal? Max = null,
    string? Regex = null,
    bool ConcurrencyToken = false,
    ConcurrencyMode ConcurrencyMode = ConcurrencyMode.RowVersion,
    bool ConcurrencyRequiredOnUpdate = true
);