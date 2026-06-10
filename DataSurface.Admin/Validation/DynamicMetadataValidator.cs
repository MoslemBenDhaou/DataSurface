using DataSurface.Admin.Dtos;
using DataSurface.Core.Enums;
using DataSurface.EFCore.Interfaces;

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
    /// <param name="staticContracts">Optional provider of STATIC resource contracts used to reject
    /// entity keys / routes that collide with statically defined resources.</param>
    /// <returns>A dictionary of errors keyed by field.</returns>
    public static IDictionary<string, string[]> Validate(AdminEntityDefDto e, IResourceContractProvider? staticContracts = null)
    {
        var errors = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

        void Add(string key, string msg)
        {
            if (!errors.TryGetValue(key, out var list)) errors[key] = list = new List<string>();
            list.Add(msg);
        }

        ValidateIdentifier(e.EntityKey, nameof(e.EntityKey), Add);
        ValidateIdentifier(e.Route, nameof(e.Route), Add);
        if (string.IsNullOrWhiteSpace(e.KeyName)) Add(nameof(e.KeyName), "KeyName is required.");

        // The dynamic CRUD router only executes Dynamic* backends; EfCore-backed definitions
        // would be routed to the EF service with no CLR entity behind them.
        if (e.Backend is not (StorageBackend.DynamicJson or StorageBackend.DynamicEav or StorageBackend.DynamicHybrid))
            Add(nameof(e.Backend), $"Backend must be a dynamic backend (DynamicJson, DynamicEav or DynamicHybrid); '{e.Backend}' is not supported for dynamic entities.");

        if (e.MaxPageSize < 1)
            Add(nameof(e.MaxPageSize), $"MaxPageSize must be at least 1 (was {e.MaxPageSize}).");
        if (e.MaxExpandDepth is < 0 or > 3)
            Add(nameof(e.MaxExpandDepth), $"MaxExpandDepth {e.MaxExpandDepth} is out of the supported range 0..3.");

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

            // A relation apiName that matches a property apiName makes the contract ambiguous.
            if (propApi.Contains(r.ApiName))
                Add($"relations.{r.ApiName}", "Relation ApiName collides with a property of the same ApiName.");

            if (r.WriteMode != RelationWriteMode.NestedDisabled &&
                string.IsNullOrWhiteSpace(r.WriteFieldName))
                Add($"relations.{r.ApiName}.writeFieldName", "WriteFieldName is required when WriteMode is enabled.");
        }

        // Key field: when no property NAME matches KeyName, the contract builder auto-injects a
        // read-only key field whose apiName is the camelCased KeyName. That injection fails at
        // runtime if the synthesized apiName collides with an existing property/relation apiName,
        // so reject the definition here instead of exploding on first use.
        if (!string.IsNullOrWhiteSpace(e.KeyName) &&
            !e.Properties.Any(p => p.Name.Equals(e.KeyName, StringComparison.OrdinalIgnoreCase)))
        {
            var injectedApi = char.ToLowerInvariant(e.KeyName[0]) + e.KeyName[1..];
            if (propApi.Contains(injectedApi) || relApi.Contains(injectedApi))
                Add(nameof(e.KeyName),
                    $"No property is named '{e.KeyName}' and the key field cannot be auto-injected: " +
                    $"its apiName '{injectedApi}' collides with an existing property or relation ApiName. " +
                    "Either add a property whose Name matches KeyName or rename the colliding member.");
        }

        // Reject collisions with statically defined resources: the composite provider prefers
        // static contracts, so a colliding dynamic entity would be silently unreachable.
        if (staticContracts is not null)
        {
            foreach (var sc in staticContracts.All)
            {
                if (!string.IsNullOrWhiteSpace(e.EntityKey) &&
                    sc.ResourceKey.Equals(e.EntityKey, StringComparison.OrdinalIgnoreCase))
                    Add(nameof(e.EntityKey), $"EntityKey '{e.EntityKey}' collides with the static resource '{sc.ResourceKey}'.");

                if (!string.IsNullOrWhiteSpace(e.Route) &&
                    sc.Route.Equals(e.Route, StringComparison.OrdinalIgnoreCase))
                    Add(nameof(e.Route), $"Route '{e.Route}' collides with the static resource '{sc.ResourceKey}' (route '{sc.Route}').");
            }
        }

        return errors.ToDictionary(k => k.Key, v => v.Value.ToArray(), StringComparer.OrdinalIgnoreCase);
    }

    private static void ValidateIdentifier(string? value, string fieldName, Action<string, string> add)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            add(fieldName, $"{fieldName} is required.");
            return;
        }

        if (value.Contains('/'))
            add(fieldName, $"{fieldName} must not contain '/'.");
        if (value.Any(char.IsWhiteSpace))
            add(fieldName, $"{fieldName} must not contain whitespace.");
    }
}
