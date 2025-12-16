using DataSurface.Admin.Dtos;

namespace DataSurface.Admin.Validation;

/// <summary>
/// Validates dynamic metadata DTOs and returns validation errors keyed by field.
/// </summary>
public static class DynamicMetadataValidator
{
    /// <summary>
    /// Validates an entity definition and returns any errors.
    /// </summary>
    /// <param name="e">The entity definition to validate.</param>
    /// <returns>A dictionary of errors keyed by field.</returns>
    public static IDictionary<string, string[]> Validate(AdminEntityDefDto e)
    {
        var errors = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

        void Add(string key, string msg)
        {
            if (!errors.TryGetValue(key, out var list)) errors[key] = list = new List<string>();
            list.Add(msg);
        }

        if (string.IsNullOrWhiteSpace(e.EntityKey)) Add(nameof(e.EntityKey), "EntityKey is required.");
        if (string.IsNullOrWhiteSpace(e.Route)) Add(nameof(e.Route), "Route is required.");
        if (string.IsNullOrWhiteSpace(e.KeyName)) Add(nameof(e.KeyName), "KeyName is required.");

        var propApi = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var p in e.Properties)
        {
            if (string.IsNullOrWhiteSpace(p.Name)) Add("properties.name", "Property Name is required.");
            if (string.IsNullOrWhiteSpace(p.ApiName)) Add("properties.apiName", "Property ApiName is required.");

            if (!propApi.Add(p.ApiName)) Add($"properties.{p.ApiName}", "Duplicate property ApiName.");
        }

        var relApi = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var r in e.Relations)
        {
            if (string.IsNullOrWhiteSpace(r.Name)) Add("relations.name", "Relation Name is required.");
            if (string.IsNullOrWhiteSpace(r.ApiName)) Add("relations.apiName", "Relation ApiName is required.");
            if (string.IsNullOrWhiteSpace(r.TargetEntityKey)) Add($"relations.{r.ApiName}", "TargetEntityKey is required.");

            if (!relApi.Add(r.ApiName)) Add($"relations.{r.ApiName}", "Duplicate relation ApiName.");

            if (r.WriteMode != DataSurface.Core.Enums.RelationWriteMode.NestedDisabled &&
                string.IsNullOrWhiteSpace(r.WriteFieldName))
                Add($"relations.{r.ApiName}.writeFieldName", "WriteFieldName is required when WriteMode is enabled.");
        }

        // key field should exist in read model (recommended)
        if (!e.Properties.Any(p => p.ApiName.Equals(e.KeyName, StringComparison.OrdinalIgnoreCase) || p.Name.Equals(e.KeyName, StringComparison.OrdinalIgnoreCase)))
        {
            // not strictly required (we store key in record table), but helps clients
        }

        return errors.ToDictionary(k => k.Key, v => v.Value.ToArray(), StringComparer.OrdinalIgnoreCase);
    }
}
