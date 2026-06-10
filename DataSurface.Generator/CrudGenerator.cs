using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace DataSurface.Generator;

/// <summary>
/// Incremental source generator that produces CRUD DTOs (and, when DataSurface.EFCore is
/// referenced, a minimal-API endpoint mapper) for types annotated with [CrudResource].
/// The semantics mirror the runtime <c>DataSurface.Core.ContractBuilder</c> with its default
/// options (opt-in fields, camelCase API names).
/// </summary>
[Generator(LanguageNames.CSharp)]
public sealed class CrudGenerator : IIncrementalGenerator
{
    private const string CrudResourceAttributeName = "DataSurface.Core.Annotations.CrudResourceAttribute";
    private const string CrudFieldAttributeName = "DataSurface.Core.Annotations.CrudFieldAttribute";
    private const string CrudKeyAttributeName = "DataSurface.Core.Annotations.CrudKeyAttribute";
    private const string CrudIgnoreAttributeName = "DataSurface.Core.Annotations.CrudIgnoreAttribute";
    private const string CrudHiddenAttributeName = "DataSurface.Core.Annotations.CrudHiddenAttribute";
    private const string CrudTenantAttributeName = "DataSurface.Core.Annotations.CrudTenantAttribute";
    private const string CrudConcurrencyAttributeName = "DataSurface.Core.Annotations.CrudConcurrencyAttribute";
    private const string CrudAuthorizeAttributeName = "DataSurface.Core.Annotations.CrudAuthorizeAttribute";
    private const string EfCoreCrudServiceMetadataName = "DataSurface.EFCore.Interfaces.IDataSurfaceCrudService";

    // CrudDto flag values (mirrors DataSurface.Core.Enums.CrudDto).
    private const int DtoRead = 1;
    private const int DtoCreate = 2;
    private const int DtoUpdate = 4;

    // CrudOperation ordinals (mirrors DataSurface.Core.Enums.CrudOperation).
    private const int OpList = 0;
    private const int OpGet = 1;
    private const int OpCreate = 2;
    private const int OpUpdate = 3;
    private const int OpDelete = 4;

    /// <summary>
    /// Fully qualified display format including nullable reference type annotations.
    /// </summary>
    private static readonly SymbolDisplayFormat TypeFormat =
        SymbolDisplayFormat.FullyQualifiedFormat.WithMiscellaneousOptions(
            SymbolDisplayFormat.FullyQualifiedFormat.MiscellaneousOptions |
            SymbolDisplayMiscellaneousOptions.IncludeNullableReferenceTypeModifier);

    /// <inheritdoc />
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var results = context.SyntaxProvider
            .ForAttributeWithMetadataName(
                CrudResourceAttributeName,
                predicate: static (node, _) => node is ClassDeclarationSyntax or RecordDeclarationSyntax,
                transform: static (ctx, ct) => Parse(ctx, ct))
            .Where(static r => r is not null)
            .Select(static (r, _) => r!);

        // DTOs + diagnostics, one output per resource declaration.
        context.RegisterSourceOutput(results, static (spc, result) =>
        {
            foreach (var d in result.Diagnostics)
            {
                spc.ReportDiagnostic(d.ToDiagnostic());
            }

            if (result.Resource is not null)
            {
                EmitDtos(spc, result.Resource);
            }
        });

        // The endpoint mapper references DataSurface.EFCore types, so only emit it when the
        // consuming compilation actually references them. A bool is value-equatable, so this
        // does not break incrementality the way combining the full Compilation would.
        var hasEfCore = context.CompilationProvider
            .Select(static (c, _) => c.GetTypeByMetadataName(EfCoreCrudServiceMetadataName) is not null);

        var mapperInput = results
            .Select(static (r, _) => r.Resource)
            .Where(static m => m is not null)
            .Select(static (m, _) => m!)
            .Collect()
            .Combine(hasEfCore);

        context.RegisterSourceOutput(mapperInput, static (spc, pair) => EmitEndpointMapper(spc, pair.Left, pair.Right));
    }

    // ---------------------------------------------------------------------------------------
    // Transform: symbols -> value-equatable model
    // ---------------------------------------------------------------------------------------

    private static ResourceResult? Parse(GeneratorAttributeSyntaxContext ctx, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        if (ctx.TargetSymbol is not INamedTypeSymbol symbol) return null;

        // The runtime assembly scanner skips abstract types; do the same.
        if (symbol.IsAbstract) return null;

        var diagnostics = new List<DiagnosticInfo>();
        var typeLocation = ctx.TargetNode.GetLocation();

        if (IsGenericOrNestedInGeneric(symbol))
        {
            diagnostics.Add(DiagnosticInfo.Create(Diagnostics.GenericResourceNotSupported, typeLocation, symbol.Name));
            return new ResourceResult(null, new EquatableArray<DiagnosticInfo>(diagnostics.ToArray()));
        }

        var resourceAttr = ctx.Attributes[0];

        var route = resourceAttr.ConstructorArguments.Length > 0
            ? resourceAttr.ConstructorArguments[0].Value as string
            : null;
        if (string.IsNullOrWhiteSpace(route))
        {
            diagnostics.Add(DiagnosticInfo.Create(Diagnostics.MissingRoute, typeLocation, symbol.Name));
            return new ResourceResult(null, new EquatableArray<DiagnosticInfo>(diagnostics.ToArray()));
        }

        var resourceKey = resourceAttr.GetNamedArgString("ResourceKey");
        if (string.IsNullOrWhiteSpace(resourceKey)) resourceKey = symbol.Name;

        var keyOverride = resourceAttr.GetNamedArgString("KeyProperty");

        var enableList = resourceAttr.GetNamedArgBool("EnableList", fallback: true);
        var enableGet = resourceAttr.GetNamedArgBool("EnableGet", fallback: true);
        var enableCreate = resourceAttr.GetNamedArgBool("EnableCreate", fallback: true);
        var enableUpdate = resourceAttr.GetNamedArgBool("EnableUpdate", fallback: true);
        var enableDelete = resourceAttr.GetNamedArgBool("EnableDelete", fallback: true);

        // ----- authorization policies ([CrudAuthorize]) ------------------------------------
        // All-operations attributes (no Operation named argument) apply first; per-operation
        // attributes always override, mirroring the runtime ContractBuilder.
        var policies = new string?[5];
        var authAttrs = symbol.GetAttributes().Where(a => a.IsAttribute(CrudAuthorizeAttributeName)).ToList();

        foreach (var auth in authAttrs.Where(a => !a.HasNamedArg("Operation")))
        {
            var policy = auth.ConstructorArguments.Length > 0 ? auth.ConstructorArguments[0].Value as string : null;
            if (string.IsNullOrWhiteSpace(policy)) continue;
            for (var i = 0; i < policies.Length; i++) policies[i] = policy;
        }

        foreach (var auth in authAttrs.Where(a => a.HasNamedArg("Operation")))
        {
            var policy = auth.ConstructorArguments.Length > 0 ? auth.ConstructorArguments[0].Value as string : null;
            if (string.IsNullOrWhiteSpace(policy)) continue;

            foreach (var kv in auth.NamedArguments)
            {
                if (kv.Key != "Operation") continue;
                // CrudOperation is an int-backed enum; the boxed constant is an int.
                if (kv.Value.Value is int op && op >= 0 && op < policies.Length)
                {
                    policies[op] = policy;
                }
            }
        }

        // ----- property collection (inherited public instance properties; closest wins) ----
        var props = CollectProperties(symbol);

        // ----- key discovery ----------------------------------------------------------------
        IPropertySymbol? keyProp;
        if (!string.IsNullOrWhiteSpace(keyOverride))
        {
            keyProp = props.FirstOrDefault(p => string.Equals(p.Name, keyOverride, StringComparison.OrdinalIgnoreCase));
            if (keyProp is null)
            {
                diagnostics.Add(DiagnosticInfo.Create(Diagnostics.KeyPropertyNotFound, typeLocation, keyOverride!, symbol.Name));
                return new ResourceResult(null, new EquatableArray<DiagnosticInfo>(diagnostics.ToArray()));
            }
        }
        else
        {
            var crudKeys = props.Where(p => p.HasAttribute(CrudKeyAttributeName)).ToList();
            if (crudKeys.Count > 1)
            {
                diagnostics.Add(DiagnosticInfo.Create(Diagnostics.MultipleKeys, typeLocation, resourceKey!));
                return new ResourceResult(null, new EquatableArray<DiagnosticInfo>(diagnostics.ToArray()));
            }

            keyProp = crudKeys.Count == 1
                ? crudKeys[0]
                : props.FirstOrDefault(p => string.Equals(p.Name, "Id", StringComparison.OrdinalIgnoreCase))
                  ?? props.FirstOrDefault(p => string.Equals(p.Name, symbol.Name + "Id", StringComparison.OrdinalIgnoreCase));

            if (keyProp is null)
            {
                diagnostics.Add(DiagnosticInfo.Create(Diagnostics.MissingKey, typeLocation, resourceKey!, symbol.Name));
                return new ResourceResult(null, new EquatableArray<DiagnosticInfo>(diagnostics.ToArray()));
            }
        }

        var keyApiOverride = keyProp.FindAttribute(CrudKeyAttributeName)?.GetNamedArgString("ApiName");

        // ----- field models -----------------------------------------------------------------
        var fields = new List<PropertyModel>();
        var usedApiNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var keyHasField = false;
        IPropertySymbol? concurrencyProp = null;
        string? tenantPropName = null;

        foreach (var p in props)
        {
            ct.ThrowIfCancellationRequested();

            var propLocation = p.Locations.FirstOrDefault();
            var fieldAttr = p.FindAttribute(CrudFieldAttributeName);

            if (p.HasAttribute(CrudIgnoreAttributeName))
            {
                if (fieldAttr is not null)
                {
                    diagnostics.Add(DiagnosticInfo.Create(Diagnostics.IgnoreFieldConflict, propLocation, p.Name, resourceKey!));
                }
                continue;
            }

            if (p.HasAttribute(CrudConcurrencyAttributeName) && concurrencyProp is null)
            {
                concurrencyProp = p;
            }

            if (p.HasAttribute(CrudTenantAttributeName) && tenantPropName is null)
            {
                tenantPropName = p.Name;
            }

            if (!IsScalarType(p.Type))
            {
                // Navigation-shaped properties never become DTO fields; [CrudField] on one is a
                // declaration we cannot honor (runtime fails loudly too).
                if (fieldAttr is not null)
                {
                    diagnostics.Add(DiagnosticInfo.Create(Diagnostics.NavigationFieldNotSupported, propLocation, p.Name, resourceKey!));
                }
                continue;
            }

            // Fields are opt-in: only [CrudField]-annotated scalars participate
            // (ContractBuilderOptions.ExposeFieldsOnlyWhenAnnotated defaults to true).
            if (fieldAttr is null) continue;

            var isKey = SymbolEqualityComparer.Default.Equals(p, keyProp);
            if (isKey) keyHasField = true;

            var inFlags = fieldAttr.ConstructorArguments.Length > 0 && fieldAttr.ConstructorArguments[0].Value is int flags ? flags : 0;

            var hidden = p.HasAttribute(CrudHiddenAttributeName) || fieldAttr.GetNamedArgBool("Hidden");
            var immutable = fieldAttr.GetNamedArgBool("Immutable") || isKey;
            var computed = !string.IsNullOrWhiteSpace(fieldAttr.GetNamedArgString("ComputedExpression"));

            var inRead = (inFlags & DtoRead) != 0 && !hidden;
            var inCreate = (inFlags & DtoCreate) != 0 && !hidden && !computed;
            var inUpdate = (inFlags & DtoUpdate) != 0 && !hidden && !immutable && !computed;

            var apiName = fieldAttr.GetNamedArgString("ApiName") ?? (isKey ? keyApiOverride : null) ?? ToCamel(p.Name);

            if (!usedApiNames.Add(apiName))
            {
                diagnostics.Add(DiagnosticInfo.Create(Diagnostics.DuplicateApiName, propLocation, apiName, resourceKey!));
                continue;
            }

            var identifier = MakeIdentifier(apiName);
            if (identifier is null)
            {
                diagnostics.Add(DiagnosticInfo.Create(Diagnostics.InvalidApiName, propLocation, apiName, p.Name, resourceKey!));
                continue;
            }

            fields.Add(CreateModel(p, apiName, identifier, inRead, inCreate, inUpdate,
                requiredOnCreate: fieldAttr.GetNamedArgBool("RequiredOnCreate")));
        }

        // Key always present in read output when it was not explicitly annotated.
        if (!keyHasField)
        {
            var apiName = keyApiOverride ?? ToCamel(keyProp.Name);
            if (!usedApiNames.Add(apiName))
            {
                diagnostics.Add(DiagnosticInfo.Create(Diagnostics.DuplicateApiName, keyProp.Locations.FirstOrDefault(), apiName, resourceKey!));
            }
            else
            {
                var identifier = MakeIdentifier(apiName);
                if (identifier is null)
                {
                    diagnostics.Add(DiagnosticInfo.Create(Diagnostics.InvalidApiName, keyProp.Locations.FirstOrDefault(), apiName, keyProp.Name, resourceKey!));
                }
                else
                {
                    fields.Add(CreateModel(keyProp, apiName, identifier, inRead: true, inCreate: false, inUpdate: false, requiredOnCreate: false));
                }
            }
        }

        // Concurrency token is auto-exposed read-only when not annotated, so clients can obtain it.
        if (concurrencyProp is not null && !fields.Any(f => string.Equals(f.Name, concurrencyProp.Name, StringComparison.OrdinalIgnoreCase)))
        {
            var apiName = ToCamel(concurrencyProp.Name);
            if (usedApiNames.Add(apiName) && MakeIdentifier(apiName) is { } identifier)
            {
                fields.Add(CreateModel(concurrencyProp, apiName, identifier, inRead: true, inCreate: false, inUpdate: false, requiredOnCreate: false));
            }
        }

        // The tenant discriminator is server-managed: never client-writable.
        if (tenantPropName is not null)
        {
            for (var i = 0; i < fields.Count; i++)
            {
                if (string.Equals(fields[i].Name, tenantPropName, StringComparison.OrdinalIgnoreCase) &&
                    (fields[i].InCreate || fields[i].InUpdate))
                {
                    fields[i] = fields[i] with { InCreate = false, InUpdate = false };
                }
            }
        }

        var keyReadIdentifier = fields
            .FirstOrDefault(f => string.Equals(f.Name, keyProp.Name, StringComparison.Ordinal) && f.InRead)
            ?.Identifier;

        var ns = symbol.ContainingNamespace;
        var isGlobalNs = ns is null || ns.IsGlobalNamespace;
        var nsName = isGlobalNs ? string.Empty : ns!.ToDisplayString();

        var model = new ResourceModel(
            Namespace: nsName,
            IsGlobalNamespace: isGlobalNs,
            EntityName: symbol.Name,
            ResourceKey: resourceKey!,
            Route: route!,
            HintName: BuildHintName(symbol, isGlobalNs, nsName),
            EnableList: enableList,
            EnableGet: enableGet,
            EnableCreate: enableCreate,
            EnableUpdate: enableUpdate,
            EnableDelete: enableDelete,
            ListPolicy: policies[OpList],
            GetPolicy: policies[OpGet],
            CreatePolicy: policies[OpCreate],
            UpdatePolicy: policies[OpUpdate],
            DeletePolicy: policies[OpDelete],
            KeyReadIdentifier: keyReadIdentifier,
            Properties: new EquatableArray<PropertyModel>(fields.ToArray()));

        return new ResourceResult(model, new EquatableArray<DiagnosticInfo>(diagnostics.ToArray()));
    }

    private static PropertyModel CreateModel(
        IPropertySymbol p, string apiName, string identifier,
        bool inRead, bool inCreate, bool inUpdate, bool requiredOnCreate)
    {
        var typeRef = p.Type.ToDisplayString(TypeFormat);
        var isNullableAnnotated = p.Type.NullableAnnotation == NullableAnnotation.Annotated;
        var typeRefNullable = isNullableAnnotated || typeRef.EndsWith("?", StringComparison.Ordinal)
            ? typeRef
            : typeRef + "?";

        return new PropertyModel(
            Name: p.Name,
            ApiName: apiName,
            Identifier: identifier,
            TypeRef: typeRef,
            TypeRefNullable: typeRefNullable,
            NeedsNullForgivingInit: p.Type.IsReferenceType && !isNullableAnnotated,
            InRead: inRead,
            InCreate: inCreate,
            InUpdate: inUpdate,
            RequiredOnCreate: requiredOnCreate);
    }

    /// <summary>
    /// Collects public instance non-indexer properties, walking base types; the closest
    /// declaration wins for shadowed names (mirrors reflection's GetProperties order).
    /// </summary>
    private static List<IPropertySymbol> CollectProperties(INamedTypeSymbol symbol)
    {
        var props = new List<IPropertySymbol>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        for (var t = symbol; t is not null && t.SpecialType != SpecialType.System_Object; t = t.BaseType)
        {
            foreach (var member in t.GetMembers())
            {
                if (member is not IPropertySymbol p) continue;
                if (p.IsStatic || p.IsIndexer || p.DeclaredAccessibility != Accessibility.Public) continue;
                if (!seen.Add(p.Name)) continue;
                props.Add(p);
            }
        }

        return props;
    }

    private static bool IsGenericOrNestedInGeneric(INamedTypeSymbol symbol)
    {
        for (var t = symbol; t is not null; t = t.ContainingType)
        {
            if (t.IsGenericType) return true;
        }
        return false;
    }

    /// <summary>
    /// Determines whether a property type is a supported scalar (mirrors ContractBuilder.IsScalar).
    /// Anything else is navigation-shaped and excluded from DTOs.
    /// </summary>
    private static bool IsScalarType(ITypeSymbol type)
    {
        if (type is INamedTypeSymbol named && named.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T)
        {
            type = named.TypeArguments[0];
        }

        if (type.TypeKind == TypeKind.Enum) return true;

        switch (type.SpecialType)
        {
            case SpecialType.System_String:
            case SpecialType.System_Boolean:
            case SpecialType.System_Char:
            case SpecialType.System_SByte:
            case SpecialType.System_Byte:
            case SpecialType.System_Int16:
            case SpecialType.System_UInt16:
            case SpecialType.System_Int32:
            case SpecialType.System_UInt32:
            case SpecialType.System_Int64:
            case SpecialType.System_UInt64:
            case SpecialType.System_Decimal:
            case SpecialType.System_Single:
            case SpecialType.System_Double:
            case SpecialType.System_DateTime:
                return true;
        }

        if (IsSystemType(type, "Guid") || IsSystemType(type, "DateTimeOffset") ||
            IsSystemType(type, "DateOnly") || IsSystemType(type, "TimeOnly") || IsSystemType(type, "TimeSpan"))
        {
            return true;
        }

        if (type is IArrayTypeSymbol { Rank: 1 } array)
        {
            var e = array.ElementType;
            return e.SpecialType is SpecialType.System_Byte or SpecialType.System_String
                       or SpecialType.System_Int32 or SpecialType.System_Decimal
                   || IsSystemType(e, "Guid");
        }

        return false;
    }

    private static bool IsSystemType(ITypeSymbol type, string name)
        => type.Name == name
           && type.ContainingNamespace is { Name: "System", ContainingNamespace.IsGlobalNamespace: true };

    private static string ToCamel(string s)
        => string.IsNullOrEmpty(s) ? s : char.ToLowerInvariant(s[0]) + s.Substring(1);

    /// <summary>
    /// PascalCases an API name into a generated property identifier, escaping keywords with
    /// '@'. Returns <c>null</c> when the result is not a valid C# identifier.
    /// </summary>
    private static string? MakeIdentifier(string apiName)
    {
        if (string.IsNullOrEmpty(apiName)) return null;

        var pascal = char.ToUpperInvariant(apiName[0]) + apiName.Substring(1);
        if (!SyntaxFacts.IsValidIdentifier(pascal)) return null;

        return SyntaxFacts.GetKeywordKind(pascal) != SyntaxKind.None || SyntaxFacts.GetContextualKeywordKind(pascal) != SyntaxKind.None
            ? "@" + pascal
            : pascal;
    }

    private static string BuildHintName(INamedTypeSymbol symbol, bool isGlobalNs, string nsName)
    {
        // Include containing types so nested entities cannot collide either.
        var typePart = symbol.Name;
        for (var t = symbol.ContainingType; t is not null; t = t.ContainingType)
        {
            typePart = t.Name + "." + typePart;
        }

        return Sanitize(isGlobalNs ? typePart : nsName + "." + typePart);

        static string Sanitize(string value)
        {
            var sb = new StringBuilder(value.Length);
            foreach (var c in value)
            {
                sb.Append(char.IsLetterOrDigit(c) || c == '_' || c == '.' ? c : '_');
            }
            return sb.ToString();
        }
    }

    // ---------------------------------------------------------------------------------------
    // Emission: DTOs
    // ---------------------------------------------------------------------------------------

    private static void EmitDtos(SourceProductionContext spc, ResourceModel r)
    {
        var sb = new StringBuilder();
        sb.AppendLine("// <auto-generated/>");
        sb.AppendLine("#nullable enable");
        sb.AppendLine();
        sb.AppendLine($"namespace {r.DtoNamespace}");
        sb.AppendLine("{");

        EmitDtoClass(sb, r, $"{r.EntityName}ReadDto", DtoShape.Read,
            $"Read DTO for the '{r.ResourceKey}' resource.");
        sb.AppendLine();
        EmitDtoClass(sb, r, $"{r.EntityName}CreateDto", DtoShape.Create,
            $"Create DTO for the '{r.ResourceKey}' resource.");
        sb.AppendLine();
        EmitDtoClass(sb, r, $"{r.EntityName}UpdateDto", DtoShape.Update,
            $"Update (PATCH) DTO for the '{r.ResourceKey}' resource; all members are optional.");

        sb.AppendLine("}");

        spc.AddSource($"{r.HintName}.DataSurfaceCrudDtos.g.cs", SourceText.From(sb.ToString(), Encoding.UTF8));
    }

    private enum DtoShape
    {
        Read,
        Create,
        Update,
    }

    private static void EmitDtoClass(StringBuilder sb, ResourceModel r, string className, DtoShape shape, string summary)
    {
        sb.AppendLine("    /// <summary>");
        sb.AppendLine($"    /// {summary}");
        sb.AppendLine("    /// </summary>");
        sb.AppendLine("    [global::System.CodeDom.Compiler.GeneratedCode(\"DataSurface.Generator\", \"1.0.0\")]");
        sb.AppendLine($"    public sealed class {className}");
        sb.AppendLine("    {");

        var first = true;
        foreach (var p in r.Properties)
        {
            var include = shape switch
            {
                DtoShape.Read => p.InRead,
                DtoShape.Create => p.InCreate,
                _ => p.InUpdate,
            };
            if (!include) continue;

            if (!first) sb.AppendLine();
            first = false;

            sb.AppendLine("        /// <summary>");
            sb.AppendLine($"        /// Maps the '{p.ApiName}' API field (CLR property '{p.Name}').");
            sb.AppendLine("        /// </summary>");
            sb.AppendLine($"        [global::System.Text.Json.Serialization.JsonPropertyName({SymbolDisplay.FormatLiteral(p.ApiName, quote: true)})]");

            if (shape == DtoShape.Create && p.RequiredOnCreate)
            {
                sb.AppendLine("        [global::System.ComponentModel.DataAnnotations.Required]");
            }

            var type = shape == DtoShape.Update ? p.TypeRefNullable : p.TypeRef;
            var initializer = shape != DtoShape.Update && p.NeedsNullForgivingInit ? " = default!;" : string.Empty;
            sb.AppendLine($"        public {type} {p.Identifier} {{ get; set; }}{initializer}");
        }

        sb.AppendLine("    }");
    }

    // ---------------------------------------------------------------------------------------
    // Emission: endpoint mapper (only when DataSurface.EFCore is referenced)
    // ---------------------------------------------------------------------------------------

    private static void EmitEndpointMapper(SourceProductionContext spc, ImmutableArray<ResourceModel> resources, bool hasEfCore)
    {
        if (!hasEfCore || resources.Length == 0) return;

        var enabled = resources
            .Where(r => r.EnableList || r.EnableGet || r.EnableCreate || r.EnableUpdate || r.EnableDelete)
            .OrderBy(r => r.HintName, StringComparer.Ordinal)
            .ToList();
        if (enabled.Count == 0) return;

        var sb = new StringBuilder();
        sb.AppendLine("// <auto-generated/>");
        sb.AppendLine("#nullable enable");
        sb.AppendLine();
        // Usings are required for extension-method resolution (MapGroup/MapGet/RequireAuthorization/LINQ);
        // everything else is referenced via global:: qualified names.
        sb.AppendLine("using global::Microsoft.AspNetCore.Builder;");
        sb.AppendLine("using global::Microsoft.AspNetCore.Http;");
        sb.AppendLine("using global::Microsoft.AspNetCore.Routing;");
        sb.AppendLine("using global::System.Linq;");
        sb.AppendLine();
        sb.AppendLine("namespace DataSurface.Generated");
        sb.AppendLine("{");
        sb.AppendLine("    /// <summary>");
        sb.AppendLine("    /// Maps minimal-API CRUD endpoints for all [CrudResource] types in this assembly.");
        sb.AppendLine("    /// </summary>");
        sb.AppendLine("    [global::System.CodeDom.Compiler.GeneratedCode(\"DataSurface.Generator\", \"1.0.0\")]");
        sb.AppendLine("    internal static class DataSurfaceGeneratedCrudEndpoints");
        sb.AppendLine("    {");
        sb.AppendLine("        /// <summary>");
        sb.AppendLine("        /// Maps the generated CRUD endpoints under <paramref name=\"apiPrefix\"/>.");
        sb.AppendLine("        /// </summary>");
        sb.AppendLine("        public static global::Microsoft.AspNetCore.Routing.IEndpointRouteBuilder MapDataSurfaceGeneratedCrud(");
        sb.AppendLine("            this global::Microsoft.AspNetCore.Routing.IEndpointRouteBuilder app, string apiPrefix = \"/api\")");
        sb.AppendLine("        {");
        sb.AppendLine("            var g = app.MapGroup(apiPrefix);");

        foreach (var r in enabled)
        {
            EmitResourceEndpoints(sb, r);
        }

        sb.AppendLine();
        sb.AppendLine("            return app;");
        sb.AppendLine("        }");
        EmitMapperHelpers(sb);
        sb.AppendLine("    }");
        sb.AppendLine("}");

        spc.AddSource("DataSurfaceGeneratedCrudEndpoints.g.cs", SourceText.From(sb.ToString(), Encoding.UTF8));
    }

    private static void EmitResourceEndpoints(StringBuilder sb, ResourceModel r)
    {
        var dtoNs = "global::" + r.DtoNamespace;
        var readDto = $"{dtoNs}.{r.EntityName}ReadDto";
        var createDto = $"{dtoNs}.{r.EntityName}CreateDto";

        var key = Lit(r.ResourceKey);
        var route = Lit("/" + r.Route.Trim('/'));
        var routeWithId = Lit("/" + r.Route.Trim('/') + "/{id}");

        sb.AppendLine();
        sb.AppendLine($"            // resource: {r.ResourceKey}");

        if (r.EnableList)
        {
            sb.AppendLine($"            g.MapGet({route}, async (global::Microsoft.AspNetCore.Http.HttpRequest req, global::DataSurface.EFCore.Interfaces.IDataSurfaceCrudService crud, global::DataSurface.EFCore.Interfaces.IResourceContractProvider contracts, global::System.Threading.CancellationToken ct) =>");
            sb.AppendLine("            {");
            sb.AppendLine($"                var contract = contracts.GetByResourceKey({key});");
            sb.AppendLine("                var spec = ParseQuerySpec(req);");
            sb.AppendLine("                var expand = ParseExpand(req, contract);");
            sb.AppendLine($"                var page = await crud.ListAsync({key}, spec, expand, ct);");
            sb.AppendLine($"                var items = page.Items.Select(x => global::System.Text.Json.JsonSerializer.Deserialize<{readDto}>(x.ToJsonString())!).ToList();");
            sb.AppendLine($"                return global::Microsoft.AspNetCore.Http.Results.Ok(new global::DataSurface.EFCore.Contracts.PagedResult<{readDto}>(items, page.Page, page.PageSize, page.Total));");
            sb.AppendLine($"            }}){AuthSuffix(r.ListPolicy)};");
        }

        if (r.EnableGet)
        {
            sb.AppendLine($"            g.MapGet({routeWithId}, async (string id, global::Microsoft.AspNetCore.Http.HttpRequest req, global::DataSurface.EFCore.Interfaces.IDataSurfaceCrudService crud, global::DataSurface.EFCore.Interfaces.IResourceContractProvider contracts, global::System.Threading.CancellationToken ct) =>");
            sb.AppendLine("            {");
            sb.AppendLine($"                var contract = contracts.GetByResourceKey({key});");
            sb.AppendLine("                var expand = ParseExpand(req, contract);");
            sb.AppendLine("                var idValue = ParseId(id, contract);");
            sb.AppendLine($"                var obj = await crud.GetAsync({key}, idValue, expand, ct);");
            sb.AppendLine("                if (obj is null) return global::Microsoft.AspNetCore.Http.Results.NotFound();");
            sb.AppendLine($"                var dto = global::System.Text.Json.JsonSerializer.Deserialize<{readDto}>(obj.ToJsonString())!;");
            sb.AppendLine("                return global::Microsoft.AspNetCore.Http.Results.Ok(dto);");
            sb.AppendLine($"            }}){AuthSuffix(r.GetPolicy)};");
        }

        if (r.EnableCreate)
        {
            // The DTO carries [JsonPropertyName] attributes, so default serializer options
            // round-trip with the runtime's camelCase wire names.
            var location = r.KeyReadIdentifier is null
                ? "$\"{req.Path}\""
                : $"$\"{{req.Path}}/{{dto.{r.KeyReadIdentifier}}}\"";

            sb.AppendLine($"            g.MapPost({route}, async ({createDto} body, global::Microsoft.AspNetCore.Http.HttpRequest req, global::DataSurface.EFCore.Interfaces.IDataSurfaceCrudService crud, global::System.Threading.CancellationToken ct) =>");
            sb.AppendLine("            {");
            sb.AppendLine("                var json = global::System.Text.Json.JsonSerializer.SerializeToNode(body)!.AsObject();");
            sb.AppendLine($"                var created = await crud.CreateAsync({key}, json, ct);");
            sb.AppendLine($"                var dto = global::System.Text.Json.JsonSerializer.Deserialize<{readDto}>(created.ToJsonString())!;");
            sb.AppendLine($"                return global::Microsoft.AspNetCore.Http.Results.Created({location}, dto);");
            sb.AppendLine($"            }}){AuthSuffix(r.CreatePolicy)};");
        }

        if (r.EnableUpdate)
        {
            // PATCH binds the raw JSON body (not the typed Update DTO) so that an omitted
            // property is distinguishable from an explicit null — preserving sparse-patch
            // semantics instead of overwriting unspecified fields with null.
            sb.AppendLine($"            g.MapMethods({routeWithId}, new[] {{ \"PATCH\" }}, async (string id, global::System.Text.Json.Nodes.JsonObject patch, global::DataSurface.EFCore.Interfaces.IDataSurfaceCrudService crud, global::DataSurface.EFCore.Interfaces.IResourceContractProvider contracts, global::System.Threading.CancellationToken ct) =>");
            sb.AppendLine("            {");
            sb.AppendLine($"                var contract = contracts.GetByResourceKey({key});");
            sb.AppendLine("                var idValue = ParseId(id, contract);");
            sb.AppendLine($"                var updated = await crud.UpdateAsync({key}, idValue, patch, ct);");
            sb.AppendLine($"                var dto = global::System.Text.Json.JsonSerializer.Deserialize<{readDto}>(updated.ToJsonString())!;");
            sb.AppendLine("                return global::Microsoft.AspNetCore.Http.Results.Ok(dto);");
            sb.AppendLine($"            }}){AuthSuffix(r.UpdatePolicy)};");
        }

        if (r.EnableDelete)
        {
            sb.AppendLine($"            g.MapDelete({routeWithId}, async (string id, global::DataSurface.EFCore.Interfaces.IDataSurfaceCrudService crud, global::DataSurface.EFCore.Interfaces.IResourceContractProvider contracts, global::System.Threading.CancellationToken ct) =>");
            sb.AppendLine("            {");
            sb.AppendLine($"                var contract = contracts.GetByResourceKey({key});");
            sb.AppendLine("                var idValue = ParseId(id, contract);");
            sb.AppendLine($"                await crud.DeleteAsync({key}, idValue, deleteSpec: null, ct);");
            sb.AppendLine("                return global::Microsoft.AspNetCore.Http.Results.NoContent();");
            sb.AppendLine($"            }}){AuthSuffix(r.DeletePolicy)};");
        }
    }

    private static void EmitMapperHelpers(StringBuilder sb)
    {
        sb.AppendLine();
        sb.AppendLine("        private static global::DataSurface.EFCore.Contracts.QuerySpec ParseQuerySpec(global::Microsoft.AspNetCore.Http.HttpRequest req)");
        sb.AppendLine("        {");
        sb.AppendLine("            int page = int.TryParse(req.Query[\"page\"], out var p) ? p : 1;");
        sb.AppendLine("            int pageSize = int.TryParse(req.Query[\"pageSize\"], out var ps) ? ps : 20;");
        sb.AppendLine("            string? sort = req.Query.TryGetValue(\"sort\", out var s) ? s.ToString() : null;");
        sb.AppendLine("            string? search = req.Query.TryGetValue(\"q\", out var q) ? q.ToString() : null;");
        sb.AppendLine("            var filters = new global::System.Collections.Generic.Dictionary<string, string>(global::System.StringComparer.OrdinalIgnoreCase);");
        sb.AppendLine("            foreach (var kv in req.Query)");
        sb.AppendLine("            {");
        sb.AppendLine("                if (!kv.Key.StartsWith(\"filter[\", global::System.StringComparison.OrdinalIgnoreCase) || !kv.Key.EndsWith(\"]\", global::System.StringComparison.Ordinal)) continue;");
        sb.AppendLine("                var field = kv.Key.Substring(\"filter[\".Length, kv.Key.Length - \"filter[\".Length - 1);");
        sb.AppendLine("                filters[field] = kv.Value.ToString();");
        sb.AppendLine("            }");
        sb.AppendLine("            return new global::DataSurface.EFCore.Contracts.QuerySpec(page, pageSize, sort, filters, search);");
        sb.AppendLine("        }");
        sb.AppendLine();
        sb.AppendLine("        private static global::DataSurface.EFCore.Contracts.ExpandSpec? ParseExpand(global::Microsoft.AspNetCore.Http.HttpRequest req, global::DataSurface.Core.Contracts.ResourceContract contract)");
        sb.AppendLine("        {");
        sb.AppendLine("            if (!req.Query.TryGetValue(\"expand\", out var exp) || string.IsNullOrWhiteSpace(exp)) return null;");
        sb.AppendLine("            var asked = exp.ToString().Split(',', global::System.StringSplitOptions.RemoveEmptyEntries | global::System.StringSplitOptions.TrimEntries).ToList();");
        sb.AppendLine("            if (asked.Count == 0) return null;");
        sb.AppendLine("            var allowed = new global::System.Collections.Generic.HashSet<string>(contract.Read.ExpandAllowed, global::System.StringComparer.OrdinalIgnoreCase);");
        sb.AppendLine("            asked = asked.Where(allowed.Contains).ToList();");
        sb.AppendLine("            return asked.Count == 0 ? null : new global::DataSurface.EFCore.Contracts.ExpandSpec(asked);");
        sb.AppendLine("        }");
        sb.AppendLine();
        sb.AppendLine("        private static object ParseId(string raw, global::DataSurface.Core.Contracts.ResourceContract contract)");
        sb.AppendLine("        {");
        sb.AppendLine("            return contract.Key.Type switch");
        sb.AppendLine("            {");
        sb.AppendLine("                global::DataSurface.Core.Enums.FieldType.Int32 => int.Parse(raw, global::System.Globalization.CultureInfo.InvariantCulture),");
        sb.AppendLine("                global::DataSurface.Core.Enums.FieldType.Int64 => long.Parse(raw, global::System.Globalization.CultureInfo.InvariantCulture),");
        sb.AppendLine("                global::DataSurface.Core.Enums.FieldType.Guid => global::System.Guid.Parse(raw),");
        sb.AppendLine("                _ => raw,");
        sb.AppendLine("            };");
        sb.AppendLine("        }");
    }

    /// <summary>
    /// Emits the ".RequireAuthorization(\"policy\")" suffix for a generated route, or "" if none.
    /// </summary>
    private static string AuthSuffix(string? policy)
        => string.IsNullOrWhiteSpace(policy)
            ? string.Empty
            : $".RequireAuthorization({Lit(policy!)})";

    private static string Lit(string value) => SymbolDisplay.FormatLiteral(value, quote: true);
}
