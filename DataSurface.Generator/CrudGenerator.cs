using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DataSurface.Generator;

/// <summary>
/// Source generator that produces CRUD DTOs and endpoint mapping helpers based on DataSurface attributes.
/// </summary>
[Generator]
public sealed class CrudGenerator : IIncrementalGenerator
{
    /// <inheritdoc />
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        // Find classes with [CrudResource]
        var candidates = context.SyntaxProvider
            .CreateSyntaxProvider(
                predicate: static (n, _) => n is ClassDeclarationSyntax cds && cds.AttributeLists.Count > 0,
                transform: static (ctx, _) => (INamedTypeSymbol?)ctx.SemanticModel.GetDeclaredSymbol((ClassDeclarationSyntax)ctx.Node))
            .Where(static s => s is not null)!
            .Select((s, _) => s!);

        var compilationAndCandidates = context.CompilationProvider.Combine(candidates.Collect());

        context.RegisterSourceOutput(compilationAndCandidates, (spc, pair) =>
        {
            var compilation = pair.Left;
            var types = pair.Right;

            var crudResourceAttr = compilation.GetTypeByMetadataName("DataSurface.Core.Annotations.CrudResourceAttribute");
            if (crudResourceAttr is null) return;

            var crudFieldAttr = compilation.GetTypeByMetadataName("DataSurface.Core.Annotations.CrudFieldAttribute");
            var crudKeyAttr = compilation.GetTypeByMetadataName("DataSurface.Core.Annotations.CrudKeyAttribute");
            var crudIgnoreAttr = compilation.GetTypeByMetadataName("DataSurface.Core.Annotations.CrudIgnoreAttribute");
            var crudDtoEnum = compilation.GetTypeByMetadataName("DataSurface.Core.Enums.CrudDto");

            if (crudFieldAttr is null || crudKeyAttr is null || crudDtoEnum is null) return;

            var resources = new List<ResourceModel>();

            foreach (var t in types.Distinct(NamedTypeSymbolComparer.Instance))
            {
                var resAttr = t.GetAttr(crudResourceAttr);
                if (resAttr is null) continue;

                // ctor arg 0 is route string
                var route = resAttr.ConstructorArguments.Length > 0 ? resAttr.ConstructorArguments[0].Value as string : null;
                if (string.IsNullOrWhiteSpace(route))
                {
                    spc.ReportDiagnostic(Diagnostic.Create(Diagnostics.MissingRoute, t.Locations.FirstOrDefault(), t.Name));
                    continue;
                }

                var resourceKey = resAttr.GetNamedArgString("ResourceKey") ?? t.Name;
                var ns = t.ContainingNamespace?.ToDisplayString() ?? "GlobalNamespace";

                // Collect properties
                var props = t.GetMembers()
                    .OfType<IPropertySymbol>()
                    .Where(p => p.DeclaredAccessibility == Accessibility.Public && !p.IsStatic)
                    .ToList();

                // Key
                var keyCandidates = props.Where(p => p.HasAttr(crudKeyAttr) || p.Name.Equals("Id", StringComparison.OrdinalIgnoreCase)).ToList();
                if (keyCandidates.Count == 0)
                {
                    spc.ReportDiagnostic(Diagnostic.Create(Diagnostics.MissingKey, t.Locations.FirstOrDefault(), resourceKey));
                    continue;
                }
                if (keyCandidates.Count > 1)
                {
                    spc.ReportDiagnostic(Diagnostic.Create(Diagnostics.MultipleKeys, t.Locations.FirstOrDefault(), resourceKey));
                    continue;
                }

                var keyProp = keyCandidates[0];
                var keyAttr = keyProp.GetAttr(crudKeyAttr);
                var keyApi = keyAttr?.GetNamedArgString("ApiName") ?? ToCamel(keyProp.Name);

                var fields = new List<PropertyModel>();
                PropertyModel? concurrency = null;

                var usedApiNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { keyApi };

                // Key model is always in read
                var keyModel = new PropertyModel(
                    Name: keyProp.Name,
                    ApiName: keyApi,
                    Type: keyProp.Type,
                    InRead: true,
                    InCreate: false,
                    InUpdate: false,
                    RequiredOnCreate: false,
                    Immutable: true,
                    Hidden: false,
                    ConcurrencyToken: false);

                foreach (var p in props)
                {
                    if (SymbolEqualityComparer.Default.Equals(p, keyProp))
                        continue;

                    if (crudIgnoreAttr is not null && p.HasAttr(crudIgnoreAttr))
                    {
                        // if ignored but also has CrudField => error
                        if (p.HasAttr(crudFieldAttr))
                            spc.ReportDiagnostic(Diagnostic.Create(Diagnostics.InvalidDtoFlags, p.Locations.FirstOrDefault(), p.Name, resourceKey));
                        continue;
                    }

                    var fa = p.GetAttr(crudFieldAttr);
                    if (fa is null) continue; // attribute-driven only in Phase 6

                    var api = fa.GetNamedArgString("ApiName") ?? ToCamel(p.Name);
                    if (!usedApiNames.Add(api))
                    {
                        spc.ReportDiagnostic(Diagnostic.Create(Diagnostics.DuplicateApiName, p.Locations.FirstOrDefault(), api, resourceKey));
                        continue;
                    }

                    // ctor arg #0 is CrudDto flags (enum)
                    var inFlagsObj = fa.ConstructorArguments.Length > 0 ? fa.ConstructorArguments[0].Value : null;
                    var inFlags = inFlagsObj is int i ? i : 0;

                    bool InRead = (inFlags & 1) != 0;   // CrudDto.Read = 1
                    bool InCreate = (inFlags & 2) != 0; // CrudDto.Create = 2
                    bool InUpdate = (inFlags & 4) != 0; // CrudDto.Update = 4

                    // Read named arguments (these are properties, not constructor args)
                    bool req = fa.GetNamedArgBool("RequiredOnCreate");
                    bool imm = fa.GetNamedArgBool("Immutable");
                    bool hid = fa.GetNamedArgBool("Hidden");
                    bool conc = p.HasAttr(compilation.GetTypeByMetadataName("DataSurface.Core.Annotations.CrudConcurrencyAttribute"));

                    var pm = new PropertyModel(
                        Name: p.Name,
                        ApiName: api,
                        Type: p.Type,
                        InRead: InRead,
                        InCreate: InCreate,
                        InUpdate: InUpdate,
                        RequiredOnCreate: req,
                        Immutable: imm,
                        Hidden: hid,
                        ConcurrencyToken: conc);

                    if (conc)
                        concurrency = pm;

                    fields.Add(pm);
                }

                resources.Add(new ResourceModel(
                    EntitySymbol: t,
                    Namespace: ns,
                    EntityName: t.Name,
                    ResourceKey: resourceKey,
                    Route: route!,
                    Key: keyModel,
                    Concurrency: concurrency,
                    Fields: fields));
            }

            // Emit DTOs + endpoint mapper
            EmitDtos(spc, resources);
            EmitEndpointMapper(spc, resources);
        });
    }

    private sealed class NamedTypeSymbolComparer : IEqualityComparer<INamedTypeSymbol>
    {
        public static readonly NamedTypeSymbolComparer Instance = new();

        public bool Equals(INamedTypeSymbol? x, INamedTypeSymbol? y) => SymbolEqualityComparer.Default.Equals(x, y);

        public int GetHashCode(INamedTypeSymbol obj) => SymbolEqualityComparer.Default.GetHashCode(obj);
    }

    private static void EmitDtos(SourceProductionContext spc, List<ResourceModel> resources)
    {
        foreach (var r in resources)
        {
            var sb = new StringBuilder();
            sb.AppendLine("// <auto-generated />");
            sb.AppendLine("using System;");
            sb.AppendLine("using System.ComponentModel.DataAnnotations;");
            sb.AppendLine();
            sb.AppendLine($"namespace {r.Namespace}.DataSurfaceGenerated;");
            sb.AppendLine();

            // Read DTO
            sb.AppendLine($"public sealed class {r.EntityName}ReadDto");
            sb.AppendLine("{");
            EmitProp(sb, r.Key, required: true, forceNonNullable: true);
            foreach (var f in r.Fields.Where(x => x.InRead && !x.Hidden))
                EmitProp(sb, f, required: false, forceNonNullable: false);
            sb.AppendLine("}");
            sb.AppendLine();

            // Create DTO
            sb.AppendLine($"public sealed class {r.EntityName}CreateDto");
            sb.AppendLine("{");
            foreach (var f in r.Fields.Where(x => x.InCreate && !x.Hidden))
                EmitProp(sb, f, required: f.RequiredOnCreate, forceNonNullable: f.RequiredOnCreate);
            sb.AppendLine("}");
            sb.AppendLine();

            // Update DTO (PATCH semantics: everything nullable unless it’s value-type already nullable)
            sb.AppendLine($"public sealed class {r.EntityName}UpdateDto");
            sb.AppendLine("{");
            foreach (var f in r.Fields.Where(x => x.InUpdate && !x.Hidden && !x.Immutable))
                EmitProp(sb, f, required: false, forceNonNullable: false, patchNullable: true);
            sb.AppendLine("}");
            sb.AppendLine();

            spc.AddSource($"{r.EntityName}.DataSurfaceGeneratedDtos.g.cs", sb.ToString());
        }
    }

    private static void EmitEndpointMapper(SourceProductionContext spc, List<ResourceModel> resources)
    {
        var sb = new StringBuilder();
        sb.AppendLine("// <auto-generated />");
        sb.AppendLine("using System;");
        sb.AppendLine("using System.Linq;");
        sb.AppendLine("using System.Text.Json;");
        sb.AppendLine("using System.Text.Json.Nodes;");
        sb.AppendLine("using System.Threading;");
        sb.AppendLine("using System.Threading.Tasks;");
        sb.AppendLine("using Microsoft.AspNetCore.Builder;");
        sb.AppendLine("using Microsoft.AspNetCore.Http;");
        sb.AppendLine("using Microsoft.AspNetCore.Routing;");
        sb.AppendLine("using DataSurface.Core.Contracts;");
        sb.AppendLine("using DataSurface.EFCore.Interfaces;");
        sb.AppendLine("using DataSurface.EFCore.Contracts;");
        sb.AppendLine();
        sb.AppendLine("namespace DataSurface.Generated;");
        sb.AppendLine();
        sb.AppendLine("public static class DataSurfaceGeneratedCrudEndpoints");
        sb.AppendLine("{");
        sb.AppendLine("  public static IEndpointRouteBuilder MapDataSurfaceGeneratedCrud(this IEndpointRouteBuilder app, string apiPrefix = \"/api\")");
        sb.AppendLine("  {");
        sb.AppendLine("    var g = app.MapGroup(apiPrefix);");

        foreach (var r in resources)
        {
            var ns = $"{r.Namespace}.DataSurfaceGenerated";
            var readDto = $"{ns}.{r.EntityName}ReadDto";
            var createDto = $"{ns}.{r.EntityName}CreateDto";
            var updateDto = $"{ns}.{r.EntityName}UpdateDto";

            var route = "/" + r.Route.Trim('/');

            sb.AppendLine($"    // {r.ResourceKey}");
            sb.AppendLine($"    g.MapGet(\"{route}\", async (HttpRequest req, IDataSurfaceCrudService crud, IResourceContractProvider contracts, CancellationToken ct) =>");
            sb.AppendLine("    {");
            sb.AppendLine($"      var contract = contracts.GetByResourceKey(\"{r.ResourceKey}\");");
            sb.AppendLine("      var spec = ParseQuerySpec(req);");
            sb.AppendLine("      var expand = ParseExpand(req, contract);");
            sb.AppendLine($"      var page = await crud.ListAsync(\"{r.ResourceKey}\", spec, expand, ct);");
            sb.AppendLine($"      var items = page.Items.Select(x => JsonSerializer.Deserialize<{readDto}>(x.ToJsonString())!).ToList();");
            sb.AppendLine($"      return Results.Ok(new PagedResult<{readDto}>(items, page.Page, page.PageSize, page.Total));");
            sb.AppendLine("    });");

            sb.AppendLine($"    g.MapGet(\"{route}/{{id}}\", async (string id, HttpRequest req, HttpResponse res, IDataSurfaceCrudService crud, IResourceContractProvider contracts, CancellationToken ct) =>");
            sb.AppendLine("    {");
            sb.AppendLine($"      var contract = contracts.GetByResourceKey(\"{r.ResourceKey}\");");
            sb.AppendLine("      var expand = ParseExpand(req, contract);");
            sb.AppendLine("      var key = ParseId(id, contract);");
            sb.AppendLine($"      var obj = await crud.GetAsync(\"{r.ResourceKey}\", key, expand, ct);");
            sb.AppendLine("      if (obj is null) return Results.NotFound();");
            sb.AppendLine($"      var dto = JsonSerializer.Deserialize<{readDto}>(obj.ToJsonString())!;");
            sb.AppendLine("      return Results.Ok(dto);");
            sb.AppendLine("    });");

            sb.AppendLine($"    g.MapPost(\"{route}\", async ({createDto} body, HttpRequest req, IDataSurfaceCrudService crud, CancellationToken ct) =>");
            sb.AppendLine("    {");
            sb.AppendLine("      var json = JsonSerializer.SerializeToNode(body)!.AsObject();");
            sb.AppendLine($"      var created = await crud.CreateAsync(\"{r.ResourceKey}\", json, ct);");
            sb.AppendLine($"      var dto = JsonSerializer.Deserialize<{readDto}>(created.ToJsonString())!;");
            sb.AppendLine("      return Results.Created(req.Path, dto);");
            sb.AppendLine("    });");

            sb.AppendLine($"    g.MapMethods(\"{route}/{{id}}\", new[] {{ \"PATCH\" }}, async (string id, {updateDto} patch, HttpRequest req, IDataSurfaceCrudService crud, IResourceContractProvider contracts, CancellationToken ct) =>");
            sb.AppendLine("    {");
            sb.AppendLine($"      var contract = contracts.GetByResourceKey(\"{r.ResourceKey}\");");
            sb.AppendLine("      var key = ParseId(id, contract);");
            sb.AppendLine("      var json = JsonSerializer.SerializeToNode(patch)!.AsObject();");
            sb.AppendLine($"      var updated = await crud.UpdateAsync(\"{r.ResourceKey}\", key, json, ct);");
            sb.AppendLine($"      var dto = JsonSerializer.Deserialize<{readDto}>(updated.ToJsonString())!;");
            sb.AppendLine("      return Results.Ok(dto);");
            sb.AppendLine("    });");

            sb.AppendLine($"    g.MapDelete(\"{route}/{{id}}\", async (string id, IDataSurfaceCrudService crud, IResourceContractProvider contracts, CancellationToken ct) =>");
            sb.AppendLine("    {");
            sb.AppendLine($"      var contract = contracts.GetByResourceKey(\"{r.ResourceKey}\");");
            sb.AppendLine("      var key = ParseId(id, contract);");
            sb.AppendLine($"      await crud.DeleteAsync(\"{r.ResourceKey}\", key, deleteSpec: null, ct);");
            sb.AppendLine("      return Results.NoContent();");
            sb.AppendLine("    });");
        }

        // helpers
        sb.AppendLine();
        sb.AppendLine("    return app;");
        sb.AppendLine("  }");
        sb.AppendLine();
        sb.AppendLine("  private static QuerySpec ParseQuerySpec(HttpRequest req)");
        sb.AppendLine("  {");
        sb.AppendLine("    int page = int.TryParse(req.Query[\"page\"], out var p) ? p : 1;");
        sb.AppendLine("    int pageSize = int.TryParse(req.Query[\"pageSize\"], out var ps) ? ps : 20;");
        sb.AppendLine("    string? sort = req.Query.TryGetValue(\"sort\", out var s) ? s.ToString() : null;");
        sb.AppendLine("    var filters = new System.Collections.Generic.Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);");
        sb.AppendLine("    foreach (var kv in req.Query)");
        sb.AppendLine("    {");
        sb.AppendLine("      if (!kv.Key.StartsWith(\"filter[\", StringComparison.OrdinalIgnoreCase) || !kv.Key.EndsWith(\"]\")) continue;");
        sb.AppendLine("      var field = kv.Key.Substring(\"filter[\".Length, kv.Key.Length - \"filter[\".Length - 1);");
        sb.AppendLine("      filters[field] = kv.Value.ToString();");
        sb.AppendLine("    }");
        sb.AppendLine("    return new QuerySpec(page, pageSize, sort, filters);");
        sb.AppendLine("  }");
        sb.AppendLine();
        sb.AppendLine("  private static ExpandSpec? ParseExpand(HttpRequest req, ResourceContract c)");
        sb.AppendLine("  {");
        sb.AppendLine("    if (!req.Query.TryGetValue(\"expand\", out var exp) || string.IsNullOrWhiteSpace(exp)) return null;");
        sb.AppendLine("    var asked = exp.ToString().Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();");
        sb.AppendLine("    if (asked.Count == 0) return null;");
        sb.AppendLine("    var allowed = new System.Collections.Generic.HashSet<string>(c.Read.ExpandAllowed, StringComparer.OrdinalIgnoreCase);");
        sb.AppendLine("    asked = asked.Where(allowed.Contains).ToList();");
        sb.AppendLine("    return asked.Count == 0 ? null : new ExpandSpec(asked);");
        sb.AppendLine("  }");
        sb.AppendLine();
        sb.AppendLine("  private static object ParseId(string raw, ResourceContract c)");
        sb.AppendLine("  {");
        sb.AppendLine("    return c.Key.Type switch");
        sb.AppendLine("    {");
        sb.AppendLine("      DataSurface.Core.Enums.FieldType.Int32 => int.Parse(raw),");
        sb.AppendLine("      DataSurface.Core.Enums.FieldType.Int64 => long.Parse(raw),");
        sb.AppendLine("      DataSurface.Core.Enums.FieldType.Guid  => Guid.Parse(raw),");
        sb.AppendLine("      _ => raw");
        sb.AppendLine("    };");
        sb.AppendLine("  }");
        sb.AppendLine("}");
        spc.AddSource("DataSurfaceGeneratedCrudEndpoints.g.cs", sb.ToString());
    }

    private static void EmitProp(StringBuilder sb, PropertyModel p, bool required, bool forceNonNullable, bool patchNullable = false)
    {
        var (typeName, nullable) = ToCSharpType(p.Type);

        // PATCH: everything nullable (except reference types are already nullable by '?')
        if (patchNullable)
        {
            if (!nullable) typeName += "?";
        }
        else
        {
            if (!forceNonNullable && nullable) typeName += "?";
        }

        if (required)
            sb.AppendLine("  [Required]");

        sb.AppendLine($"  public {typeName} {ToPascal(p.ApiName)} {{ get; set; }}");
        sb.AppendLine();
    }

    private static (string typeName, bool nullable) ToCSharpType(ITypeSymbol t)
    {
        // Keep it simple: emit the symbol’s display name (works for most app types)
        // Determine nullability by checking if it’s a reference type or nullable value type
        var name = t.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat);

        bool isNullableValueType = name.EndsWith("?");
        bool isRef = t.IsReferenceType;

        return (name.TrimEnd('?'), isNullableValueType || isRef);
    }

    private static string ToCamel(string s)
        => string.IsNullOrEmpty(s) ? s : char.ToLowerInvariant(s[0]) + s[1..];

    private static string ToPascal(string s)
        => string.IsNullOrEmpty(s) ? s : char.ToUpperInvariant(s[0]) + s[1..];
}
