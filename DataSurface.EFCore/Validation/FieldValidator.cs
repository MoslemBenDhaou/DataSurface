using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using DataSurface.Core.Contracts;
using DataSurface.Core.Enums;
using DataSurface.EFCore.Exceptions;

namespace DataSurface.EFCore.Validation;

/// <summary>
/// Shared field-level validation logic for resource contract fields.
/// Used by both EF Core and Dynamic CRUD services.
/// </summary>
public static class FieldValidator
{
    /// <summary>
    /// Validates field-level constraints (MinLength, MaxLength, Min, Max, Regex, AllowedValues)
    /// on a JSON body against the resource contract.
    /// </summary>
    /// <param name="contract">The resource contract.</param>
    /// <param name="body">The JSON body to validate.</param>
    /// <param name="errors">The error dictionary to append to.</param>
    public static void ValidateFieldConstraints(
        ResourceContract contract,
        JsonObject body,
        Dictionary<string, string[]> errors)
    {
        foreach (var kv in body)
        {
            var field = contract.Fields.FirstOrDefault(f => f.ApiName.Equals(kv.Key, StringComparison.OrdinalIgnoreCase));
            if (field is null || field.Hidden) continue;

            var val = field.Validation;
            var node = kv.Value;
            var fieldErrors = new List<string>();

            // String validations
            if (node is not null && field.Type == FieldType.String)
            {
                var strVal = node.GetValue<string?>();
                if (strVal is not null)
                {
                    if (val.MinLength.HasValue && strVal.Length < val.MinLength.Value)
                        fieldErrors.Add($"Minimum length is {val.MinLength.Value}.");

                    if (val.MaxLength.HasValue && strVal.Length > val.MaxLength.Value)
                        fieldErrors.Add($"Maximum length is {val.MaxLength.Value}.");

                    if (!string.IsNullOrEmpty(val.Regex))
                    {
                        try
                        {
                            if (!Regex.IsMatch(strVal, val.Regex))
                                fieldErrors.Add($"Value does not match required pattern.");
                        }
                        catch (ArgumentException)
                        {
                            // Invalid regex pattern in contract configuration - treat as validation error
                            fieldErrors.Add($"Field has invalid validation pattern configured. Contact administrator.");
                        }
                    }

                    if (val.AllowedValues is { Count: > 0 })
                    {
                        if (!val.AllowedValues.Contains(strVal, StringComparer.OrdinalIgnoreCase))
                            fieldErrors.Add($"Value must be one of: {string.Join(", ", val.AllowedValues)}.");
                    }
                }
            }

            // Numeric validations (decimal covers int, long, etc.)
            if (node is not null && IsNumericFieldType(field.Type))
            {
                try
                {
                    var numVal = node.GetValue<decimal>();

                    if (val.Min.HasValue && numVal < val.Min.Value)
                        fieldErrors.Add($"Minimum value is {val.Min.Value}.");

                    if (val.Max.HasValue && numVal > val.Max.Value)
                        fieldErrors.Add($"Maximum value is {val.Max.Value}.");
                }
                catch (FormatException)
                {
                    fieldErrors.Add($"Value must be a valid number.");
                }
                catch (InvalidOperationException)
                {
                    fieldErrors.Add($"Value must be a valid number.");
                }
            }

            if (fieldErrors.Count > 0)
                errors[kv.Key] = fieldErrors.ToArray();
        }
    }

    /// <summary>
    /// Returns whether the given field type is numeric.
    /// </summary>
    public static bool IsNumericFieldType(FieldType type)
        => type is FieldType.Int32 or FieldType.Int64 or FieldType.Decimal;
}
