namespace DataSurface.Core.Contracts;

/// <summary>
/// Validation rules associated with a field.
/// </summary>
/// <param name="RequiredOnCreate">Whether the field must be present on create.</param>
/// <param name="MinLength">Minimum allowed length for string values.</param>
/// <param name="MaxLength">Maximum allowed length for string values.</param>
/// <param name="Min">Minimum allowed numeric value.</param>
/// <param name="Max">Maximum allowed numeric value.</param>
/// <param name="Regex">Optional regex pattern constraint for string values.</param>
public sealed record FieldValidationContract(
    bool RequiredOnCreate,
    int? MinLength,
    int? MaxLength,
    decimal? Min,
    decimal? Max,
    string? Regex
);