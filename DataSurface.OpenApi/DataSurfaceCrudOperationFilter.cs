using DataSurface.Core.Enums;
using DataSurface.EFCore.Interfaces;
using DataSurface.Http;
using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace DataSurface.OpenApi;

/// <summary>
/// Swashbuckle operation filter that customizes DataSurface CRUD endpoints using dependency injection.
/// </summary>
public sealed class DataSurfaceCrudOperationFilter : IOperationFilter
{
    private readonly IResourceContractProvider _contracts;

    /// <summary>
    /// Creates a new instance of the operation filter.
    /// </summary>
    /// <param name="contracts">Contract provider used to look up resource contracts.</param>
    public DataSurfaceCrudOperationFilter(IResourceContractProvider contracts)
        => _contracts = contracts;

    /// <summary>
    /// Applies DataSurface-specific request/response schemas and query parameters to a Swagger operation.
    /// </summary>
    /// <param name="operation">The operation to modify.</param>
    /// <param name="context">The filter context.</param>
    public void Apply(OpenApiOperation operation, OperationFilterContext context)
    {
        var meta = context.ApiDescription.ActionDescriptor.EndpointMetadata
            .OfType<DataSurfaceCrudEndpointMetadata>()
            .FirstOrDefault();

        if (meta is null) return;

        if (meta.ResourceKey == "*")
        {
            if (meta.Operation == CrudOperation.List)
            {
                operation.Parameters ??= new List<OpenApiParameter>();

                operation.Parameters.Add(new OpenApiParameter { Name = "page", In = ParameterLocation.Query, Schema = new OpenApiSchema { Type = "integer" } });
                operation.Parameters.Add(new OpenApiParameter { Name = "pageSize", In = ParameterLocation.Query, Schema = new OpenApiSchema { Type = "integer" } });
                operation.Parameters.Add(new OpenApiParameter { Name = "sort", In = ParameterLocation.Query, Schema = new OpenApiSchema { Type = "string" } });
                operation.Parameters.Add(new OpenApiParameter { Name = "expand", In = ParameterLocation.Query, Schema = new OpenApiSchema { Type = "string" } });

                operation.Description = (operation.Description ?? "")
                    + "\n\nFilters: use `filter[field]=op:value` (e.g. `filter[name]=contains:abc`).";
            }

            if (meta.Operation == CrudOperation.Get)
            {
                operation.Parameters ??= new List<OpenApiParameter>();
                operation.Parameters.Add(new OpenApiParameter { Name = "expand", In = ParameterLocation.Query, Schema = new OpenApiSchema { Type = "string" } });
            }

            return;
        }

        var c = _contracts.GetByResourceKey(meta.ResourceKey);

        // Describe common query params for list
        if (meta.Operation == CrudOperation.List)
        {
            operation.Parameters ??= new List<OpenApiParameter>();

            operation.Parameters.Add(new OpenApiParameter { Name = "page", In = ParameterLocation.Query, Schema = new OpenApiSchema { Type = "integer" } });
            operation.Parameters.Add(new OpenApiParameter { Name = "pageSize", In = ParameterLocation.Query, Schema = new OpenApiSchema { Type = "integer" } });
            operation.Parameters.Add(new OpenApiParameter { Name = "sort", In = ParameterLocation.Query, Schema = new OpenApiSchema { Type = "string" } });
            operation.Parameters.Add(new OpenApiParameter { Name = "expand", In = ParameterLocation.Query, Schema = new OpenApiSchema { Type = "string" } });

            // Inform how filters work
            operation.Description = (operation.Description ?? "")
                + "\n\nFilters: use `filter[field]=op:value` (e.g. `filter[name]=contains:abc`).";
        }

        // Request/response schemas (override JsonObject)
        var readName = $"{c.ResourceKey}.Read";
        var createName = $"{c.ResourceKey}.Create";
        var updateName = $"{c.ResourceKey}.Update";

        EnsureSchema(context, readName, DataSurfaceSchemaBuilder.BuildReadSchema(c));
        EnsureSchema(context, createName, DataSurfaceSchemaBuilder.BuildCreateSchema(c));
        EnsureSchema(context, updateName, DataSurfaceSchemaBuilder.BuildUpdateSchema(c));

        if (meta.Operation == CrudOperation.Get)
        {
            if (TryGetJsonContent(operation.Responses, "200", out var content))
                content.Schema = Ref(readName);
        }
        else if (meta.Operation == CrudOperation.Create)
        {
            if (operation.RequestBody?.Content?.TryGetValue("application/json", out var reqContent) == true)
                reqContent.Schema = Ref(createName);
            if (TryGetJsonContent(operation.Responses, "201", out var resContent))
                resContent.Schema = Ref(readName);
        }
        else if (meta.Operation == CrudOperation.Update)
        {
            if (operation.RequestBody?.Content?.TryGetValue("application/json", out var reqContent) == true)
                reqContent.Schema = Ref(updateName);
            if (TryGetJsonContent(operation.Responses, "200", out var resContent))
                resContent.Schema = Ref(readName);
        }
        else if (meta.Operation == CrudOperation.List)
        {
            // PagedResult<Read> as object schema
            var pageName = $"{c.ResourceKey}.Paged";
            EnsureSchema(context, pageName, new OpenApiSchema
            {
                Type = "object",
                Properties = new Dictionary<string, OpenApiSchema>
                {
                    ["items"] = new() { Type = "array", Items = Ref(readName) },
                    ["page"] = new() { Type = "integer" },
                    ["pageSize"] = new() { Type = "integer" },
                    ["total"] = new() { Type = "integer" }
                },
                AdditionalPropertiesAllowed = false
            });

            if (TryGetJsonContent(operation.Responses, "200", out var content))
                content.Schema = Ref(pageName);
        }
    }

    private static void EnsureSchema(OperationFilterContext ctx, string name, OpenApiSchema schema)
    {
        if (!ctx.SchemaRepository.Schemas.ContainsKey(name))
            ctx.SchemaRepository.Schemas[name] = schema;
    }

    private static OpenApiSchema Ref(string name)
        => new() { Reference = new OpenApiReference { Type = ReferenceType.Schema, Id = name } };

    private static bool TryGetJsonContent(
        OpenApiResponses responses,
        string statusCode,
        out OpenApiMediaType content)
    {
        content = null!;
        if (responses == null) return false;
        if (!responses.TryGetValue(statusCode, out var response)) return false;
        if (response?.Content == null) return false;
        return response.Content.TryGetValue("application/json", out content!);
    }
}
