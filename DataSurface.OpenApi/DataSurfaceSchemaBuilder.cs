using System.Globalization;
using System.Text.Json.Nodes;
using DataSurface.Core.Contracts;
using DataSurface.Core.Enums;
using Microsoft.OpenApi;

namespace DataSurface.OpenApi;

/// <summary>
/// Builds OpenAPI schemas for DataSurface resources based on a <see cref="ResourceContract"/>.
/// </summary>
public static class DataSurfaceSchemaBuilder
{
    /// <summary>
    /// Builds the schema representing the read (response) shape for the resource.
    /// </summary>
    public static OpenApiSchema BuildReadSchema(ResourceContract c)
        => BuildSchemaFromFields(c, f => f.InRead && !f.Hidden);

    /// <summary>
    /// Builds the schema representing the create (request) shape for the resource.
    /// </summary>
    public static OpenApiSchema BuildCreateSchema(ResourceContract c)
        => BuildSchemaFromOp(c, CrudOperation.Create);

    /// <summary>
    /// Builds the schema representing the update (request) shape for the resource.
    /// </summary>
    public static OpenApiSchema BuildUpdateSchema(ResourceContract c)
        => BuildSchemaFromOp(c, CrudOperation.Update);

    private static OpenApiSchema BuildSchemaFromOp(ResourceContract c, CrudOperation op)
    {
        var oc = c.Operations[op];
        var allowed = new HashSet<string>(oc.InputShape, StringComparer.OrdinalIgnoreCase);

        var schema = BuildSchemaFromFields(c, f => allowed.Contains(f.ApiName));

        if (op == CrudOperation.Create)
            schema.Required = new HashSet<string>(oc.RequiredOnCreate, StringComparer.OrdinalIgnoreCase);

        return schema;
    }

    private static OpenApiSchema BuildSchemaFromFields(ResourceContract c, Func<FieldContract, bool> pick)
    {
        // Microsoft.OpenApi v2 typed Properties as IDictionary<string, IOpenApiSchema>
        // (the interface) so references and inline schemas can both live in the same map.
        var props = new Dictionary<string, IOpenApiSchema>(StringComparer.OrdinalIgnoreCase);

        foreach (var f in c.Fields.Where(pick))
        {
            props[f.ApiName] = FieldSchema(f);
        }

        return new OpenApiSchema
        {
            Type = JsonSchemaType.Object,
            Properties = props,
            AdditionalPropertiesAllowed = false
        };
    }

    private static OpenApiSchema FieldSchema(FieldContract f)
    {
        // Microsoft.OpenApi v2 changed Type from string to a flags enum (JsonSchemaType)
        // to natively express OpenAPI 3.1's `type: ["string", "null"]` form.
        var s = f.Type switch
        {
            FieldType.Int32 => new OpenApiSchema { Type = JsonSchemaType.Integer, Format = "int32" },
            FieldType.Int64 => new OpenApiSchema { Type = JsonSchemaType.Integer, Format = "int64" },
            FieldType.Decimal => new OpenApiSchema { Type = JsonSchemaType.Number, Format = "decimal" },
            FieldType.Boolean => new OpenApiSchema { Type = JsonSchemaType.Boolean },
            FieldType.Guid => new OpenApiSchema { Type = JsonSchemaType.String, Format = "uuid" },
            FieldType.DateTime => new OpenApiSchema { Type = JsonSchemaType.String, Format = "date-time" },
            FieldType.Json => new OpenApiSchema { Type = JsonSchemaType.Object },
            FieldType.Enum => new OpenApiSchema { Type = JsonSchemaType.String },
            FieldType.StringArray => new OpenApiSchema { Type = JsonSchemaType.Array, Items = new OpenApiSchema { Type = JsonSchemaType.String } },
            FieldType.IntArray => new OpenApiSchema { Type = JsonSchemaType.Array, Items = new OpenApiSchema { Type = JsonSchemaType.Integer } },
            FieldType.GuidArray => new OpenApiSchema { Type = JsonSchemaType.Array, Items = new OpenApiSchema { Type = JsonSchemaType.String, Format = "uuid" } },
            FieldType.DecimalArray => new OpenApiSchema { Type = JsonSchemaType.Array, Items = new OpenApiSchema { Type = JsonSchemaType.Number } },
            _ => new OpenApiSchema { Type = JsonSchemaType.String }
        };

        // OpenAPI 3.1 represents nullability via the type set. Microsoft.OpenApi v2
        // dropped the v1 `Nullable` boolean in favour of `JsonSchemaType.Null` flag.
        if (f.Nullable && s.Type.HasValue)
            s.Type = s.Type.Value | JsonSchemaType.Null;

        var v = f.Validation;

        if (f.Type == FieldType.String)
        {
            if (v.MinLength.HasValue)
                s.MinLength = v.MinLength.Value;
            if (v.MaxLength.HasValue)
                s.MaxLength = v.MaxLength.Value;
            if (!string.IsNullOrEmpty(v.Regex))
                s.Pattern = v.Regex;
        }

        if (f.Type is FieldType.Int32 or FieldType.Int64 or FieldType.Decimal)
        {
            // Min/Max moved from decimal to string in v2 (to support arbitrary precision).
            if (v.Min.HasValue)
                s.Minimum = v.Min.Value.ToString(CultureInfo.InvariantCulture);
            if (v.Max.HasValue)
                s.Maximum = v.Max.Value.ToString(CultureInfo.InvariantCulture);
        }

        // Enum is List<JsonNode> in v2 (replaced the v1 IOpenApiAny abstraction with
        // the System.Text.Json node tree).
        if (v.AllowedValues is { Count: > 0 })
        {
            s.Enum = v.AllowedValues
                .Select(val => (JsonNode)JsonValue.Create(val)!)
                .ToList();
        }

        return s;
    }
}
