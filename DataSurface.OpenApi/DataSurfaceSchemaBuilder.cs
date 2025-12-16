using DataSurface.Core.Contracts;
using DataSurface.Core.Enums;
using Microsoft.OpenApi.Models;

namespace DataSurface.OpenApi;

/// <summary>
/// Builds OpenAPI schemas for DataSurface resources based on a <see cref="ResourceContract"/>.
/// </summary>
public static class DataSurfaceSchemaBuilder
{
    /// <summary>
    /// Builds the schema representing the read (response) shape for the resource.
    /// </summary>
    /// <param name="c">The resource contract.</param>
    /// <returns>The generated OpenAPI schema.</returns>
    public static OpenApiSchema BuildReadSchema(ResourceContract c)
        => BuildSchemaFromFields(c, f => f.InRead && !f.Hidden);

    /// <summary>
    /// Builds the schema representing the create (request) shape for the resource.
    /// </summary>
    /// <param name="c">The resource contract.</param>
    /// <returns>The generated OpenAPI schema.</returns>
    public static OpenApiSchema BuildCreateSchema(ResourceContract c)
        => BuildSchemaFromOp(c, CrudOperation.Create);

    /// <summary>
    /// Builds the schema representing the update (request) shape for the resource.
    /// </summary>
    /// <param name="c">The resource contract.</param>
    /// <returns>The generated OpenAPI schema.</returns>
    public static OpenApiSchema BuildUpdateSchema(ResourceContract c)
        => BuildSchemaFromOp(c, CrudOperation.Update);

    private static OpenApiSchema BuildSchemaFromOp(ResourceContract c, CrudOperation op)
    {
        var oc = c.Operations[op];
        var allowed = new HashSet<string>(oc.InputShape, StringComparer.OrdinalIgnoreCase);

        // input shape schema only contains allowed fields; required contains required-on-create
        var schema = BuildSchemaFromFields(c, f => allowed.Contains(f.ApiName));

        if (op == CrudOperation.Create)
            schema.Required = new HashSet<string>(oc.RequiredOnCreate, StringComparer.OrdinalIgnoreCase);

        return schema;
    }

    private static OpenApiSchema BuildSchemaFromFields(ResourceContract c, Func<FieldContract, bool> pick)
    {
        var props = new Dictionary<string, OpenApiSchema>(StringComparer.OrdinalIgnoreCase);

        foreach (var f in c.Fields.Where(pick))
        {
            props[f.ApiName] = FieldSchema(f.Type, f.Nullable);
        }

        return new OpenApiSchema
        {
            Type = "object",
            Properties = props,
            AdditionalPropertiesAllowed = false
        };
    }

    private static OpenApiSchema FieldSchema(FieldType t, bool nullable)
    {
        var s = t switch
        {
            FieldType.Int32 => new OpenApiSchema { Type = "integer", Format = "int32" },
            FieldType.Int64 => new OpenApiSchema { Type = "integer", Format = "int64" },
            FieldType.Decimal => new OpenApiSchema { Type = "number", Format = "decimal" },
            FieldType.Boolean => new OpenApiSchema { Type = "boolean" },
            FieldType.Guid => new OpenApiSchema { Type = "string", Format = "uuid" },
            FieldType.DateTime => new OpenApiSchema { Type = "string", Format = "date-time" },
            FieldType.Json => new OpenApiSchema { Type = "object" },
            FieldType.Enum => new OpenApiSchema { Type = "string" },
            FieldType.StringArray => new OpenApiSchema { Type = "array", Items = new OpenApiSchema { Type = "string" } },
            FieldType.IntArray => new OpenApiSchema { Type = "array", Items = new OpenApiSchema { Type = "integer" } },
            FieldType.GuidArray => new OpenApiSchema { Type = "array", Items = new OpenApiSchema { Type = "string", Format = "uuid" } },
            FieldType.DecimalArray => new OpenApiSchema { Type = "array", Items = new OpenApiSchema { Type = "number" } },
            _ => new OpenApiSchema { Type = "string" }
        };

        s.Nullable = nullable;
        return s;
    }
}
