using System.Text.Json.Nodes;
using DataSurface.Core.Contracts;
using DataSurface.EFCore.Interfaces;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace DataSurface.Http;

/// <summary>
/// Maps an endpoint that exposes JSON Schema for DataSurface resources.
/// </summary>
public static class DataSurfaceSchemaEndpoint
{
    /// <summary>
    /// Maps the schema endpoint under the provided route group.
    /// </summary>
    /// <param name="group">The route group to register the endpoint on.</param>
    public static void MapSchema(RouteGroupBuilder group)
    {
        group.MapGet("/$schema/{resourceKey}", (string resourceKey, IResourceContractProvider provider) =>
        {
            var contract = provider.All.FirstOrDefault(c => 
                c.ResourceKey.Equals(resourceKey, StringComparison.OrdinalIgnoreCase) ||
                c.Route.Equals(resourceKey, StringComparison.OrdinalIgnoreCase));

            if (contract is null)
                return Results.NotFound(new { error = $"Resource '{resourceKey}' not found." });

            var schema = BuildJsonSchema(contract);
            return Results.Ok(schema);
        })
        .WithName("DataSurface.Schema")
        .WithTags("DataSurface");
    }

    private static JsonObject BuildJsonSchema(ResourceContract c)
    {
        var properties = new JsonObject();
        var required = new JsonArray();

        foreach (var field in c.Fields.Where(f => f.InRead && !f.Hidden))
        {
            var fieldSchema = new JsonObject
            {
                ["type"] = MapFieldTypeToJsonType(field.Type),
            };

            if (!string.IsNullOrEmpty(MapFieldTypeToFormat(field.Type)))
                fieldSchema["format"] = MapFieldTypeToFormat(field.Type);

            if (field.Nullable)
                fieldSchema["nullable"] = true;

            if (field.Validation is { } v)
            {
                if (v.MinLength.HasValue) fieldSchema["minLength"] = v.MinLength.Value;
                if (v.MaxLength.HasValue) fieldSchema["maxLength"] = v.MaxLength.Value;
                if (v.Min.HasValue) fieldSchema["minimum"] = v.Min.Value;
                if (v.Max.HasValue) fieldSchema["maximum"] = v.Max.Value;
                if (!string.IsNullOrEmpty(v.Regex)) fieldSchema["pattern"] = v.Regex;
            }

            properties[field.ApiName] = fieldSchema;
        }

        // Required on create
        var createOp = c.Operations.GetValueOrDefault(Core.Enums.CrudOperation.Create);
        if (createOp is not null)
        {
            foreach (var req in createOp.RequiredOnCreate)
            {
                required.Add(req);
            }
        }

        var schema = new JsonObject
        {
            ["$schema"] = "https://json-schema.org/draft/2020-12/schema",
            ["$id"] = $"urn:datasurface:{c.ResourceKey}",
            ["title"] = c.ResourceKey,
            ["type"] = "object",
            ["properties"] = properties,
            ["additionalProperties"] = false
        };

        if (required.Count > 0)
            schema["required"] = required;

        // Add relations info
        if (c.Relations.Count > 0)
        {
            var relations = new JsonObject();
            foreach (var rel in c.Relations)
            {
                var isCollection = rel.Kind == Core.Enums.RelationKind.OneToMany || 
                                   rel.Kind == Core.Enums.RelationKind.ManyToMany;
                relations[rel.ApiName] = new JsonObject
                {
                    ["type"] = isCollection ? "array" : "object",
                    ["$ref"] = $"urn:datasurface:{rel.TargetResourceKey}",
                    ["expandable"] = c.Read.ExpandAllowed.Contains(rel.ApiName, StringComparer.OrdinalIgnoreCase)
                };
            }
            schema["x-relations"] = relations;
        }

        // Add operations info
        var operations = new JsonObject();
        foreach (var (op, opContract) in c.Operations)
        {
            if (!opContract.Enabled) continue;
            operations[op.ToString().ToLowerInvariant()] = new JsonObject
            {
                ["enabled"] = true,
                ["inputFields"] = new JsonArray(opContract.InputShape.Select(x => JsonValue.Create(x)).ToArray()),
                ["requiredOnCreate"] = new JsonArray(opContract.RequiredOnCreate.Select(x => JsonValue.Create(x)).ToArray()),
                ["immutableFields"] = new JsonArray(opContract.ImmutableFields.Select(x => JsonValue.Create(x)).ToArray())
            };
        }
        schema["x-operations"] = operations;

        // Query info
        schema["x-query"] = new JsonObject
        {
            ["maxPageSize"] = c.Query.MaxPageSize,
            ["filterableFields"] = new JsonArray(c.Query.FilterableFields.Select(x => JsonValue.Create(x)).ToArray()),
            ["sortableFields"] = new JsonArray(c.Query.SortableFields.Select(x => JsonValue.Create(x)).ToArray())
        };

        return schema;
    }

    private static string MapFieldTypeToJsonType(Core.Enums.FieldType type) => type switch
    {
        Core.Enums.FieldType.Int32 or Core.Enums.FieldType.Int64 => "integer",
        Core.Enums.FieldType.Decimal => "number",
        Core.Enums.FieldType.Boolean => "boolean",
        Core.Enums.FieldType.StringArray or Core.Enums.FieldType.IntArray or 
            Core.Enums.FieldType.GuidArray or Core.Enums.FieldType.DecimalArray => "array",
        Core.Enums.FieldType.Json => "object",
        _ => "string"
    };

    private static string? MapFieldTypeToFormat(Core.Enums.FieldType type) => type switch
    {
        Core.Enums.FieldType.Int32 => "int32",
        Core.Enums.FieldType.Int64 => "int64",
        Core.Enums.FieldType.Guid => "uuid",
        Core.Enums.FieldType.DateTime => "date-time",
        _ => null
    };
}
