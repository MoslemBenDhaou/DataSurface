using System.Reflection;
using DataSurface.Core.Annotations;
using DataSurface.Core.ContractBuilderModels;
using DataSurface.Core.Contracts;
using DataSurface.Core.Enums;

namespace DataSurface.Core;

/// <summary>
/// Builds a unified <see cref="ResourceContract"/> model from CLR types annotated with CRUD attributes.
/// </summary>
/// <remarks>
/// The builder applies opt-in exposure and generates allowlists for read/write shapes, filtering, sorting,
/// relation expansion, concurrency, and per-operation security policies.
/// </remarks>
public sealed class ContractBuilder
{
    private readonly ContractBuilderOptions _opt;

    /// <summary>
    /// Creates a new builder.
    /// </summary>
    /// <param name="options">Optional configuration controlling safe defaults and field exposure.</param>
    public ContractBuilder(ContractBuilderOptions? options = null)
        => _opt = options ?? new ContractBuilderOptions();

    /// <summary>
    /// Scans an assembly for types annotated with <see cref="CrudResourceAttribute"/> and builds their contracts.
    /// </summary>
    /// <param name="assembly">Assembly to scan.</param>
    /// <returns>The generated resource contracts.</returns>
    /// <exception cref="DataSurface.Core.ContractBuilder.ContractValidationException">Thrown when the generated contracts fail validation.</exception>
    public IReadOnlyList<ResourceContract> BuildFromAssembly(Assembly assembly)
    {
        var types = assembly.GetTypes()
            .Where(t => t.IsClass && !t.IsAbstract && t.GetCustomAttribute<CrudResourceAttribute>() != null)
            .ToArray();

        return BuildFromTypes(types);
    }

    /// <summary>
    /// Builds contracts from the provided resource CLR types.
    /// </summary>
    /// <param name="resourceTypes">Resource types to build contracts for.</param>
    /// <returns>The generated resource contracts.</returns>
    /// <exception cref="DataSurface.Core.ContractBuilder.ContractValidationException">Thrown when the generated contracts fail validation.</exception>
    public IReadOnlyList<ResourceContract> BuildFromTypes(params Type[] resourceTypes)
    {
        // First pass: create resource stubs to resolve targets by resourceKey
        var resourceAttrs = resourceTypes.Select(t => (Type: t, Attr: t.GetCustomAttribute<CrudResourceAttribute>()!)).ToList();
        var keyMap = resourceAttrs.ToDictionary(
            x => x.Attr.ResourceKey ?? x.Type.Name,
            x => x.Type,
            StringComparer.OrdinalIgnoreCase
        );

        var contracts = resourceAttrs.Select(x => BuildSingle(x.Type, x.Attr, keyMap)).ToList();

        ValidateAll(contracts);

        return contracts;
    }

    private ResourceContract BuildSingle(Type clrType, CrudResourceAttribute ra, IDictionary<string, Type> keyMap)
    {
        var resourceKey = ra.ResourceKey ?? clrType.Name;

        var (keyName, keyType) = DiscoverKey(clrType, ra.KeyProperty);
        var key = new ResourceKeyContract(keyName, keyType);

        // security policies: default naming; can be overridden by [CrudAuthorize]
        var policies = new Dictionary<CrudOperation, string?>()
        {
            [CrudOperation.List]   = $"{ra.Route}.read",
            [CrudOperation.Get]    = $"{ra.Route}.read",
            [CrudOperation.Create] = $"{ra.Route}.create",
            [CrudOperation.Update] = $"{ra.Route}.update",
            [CrudOperation.Delete] = $"{ra.Route}.delete",
        };

        foreach (var auth in clrType.GetCustomAttributes<CrudAuthorizeAttribute>())
        {
            if (auth.Operation is null)
            {
                foreach (var op in policies.Keys.ToArray())
                    policies[op] = auth.Policy;
            }
            else
            {
                policies[auth.Operation.Value] = auth.Policy;
            }
        }

        // Fields + relations
        var fields = new List<FieldContract>();
        var relations = new List<RelationContract>();

        ConcurrencyContract? concurrency = null;

        foreach (var p in clrType.GetProperties(BindingFlags.Instance | BindingFlags.Public))
        {
            if (p.GetIndexParameters().Length > 0) continue;

            // Concurrency field
            var cc = p.GetCustomAttribute<CrudConcurrencyAttribute>();
            if (cc != null)
            {
                var apiName = ToApiName(p.Name);
                concurrency = new ConcurrencyContract(cc.Mode, apiName, cc.RequiredOnUpdate);
                // still treat it as a field if annotated or default-included
            }

            if (IsNavigationProperty(p))
            {
                var relAttr = p.GetCustomAttribute<CrudRelationAttribute>();
                if (relAttr == null) continue; // safe default: relations opt-in
                relations.Add(BuildRelation(p, relAttr, keyMap));
                continue;
            }

            // scalar field
            var hidden = p.GetCustomAttribute<CrudHiddenAttribute>() != null;

            var fa = p.GetCustomAttribute<CrudFieldAttribute>();

            if (_opt.ExposeFieldsOnlyWhenAnnotated && fa == null)
                continue;

            var (inRead, inCreate, inUpdate, filterable, sortable) = fa == null
                ? DefaultScalarMembership()
                : (
                    fa.In.HasFlag(CrudDto.Read),
                    fa.In.HasFlag(CrudDto.Create),
                    fa.In.HasFlag(CrudDto.Update),
                    fa.In.HasFlag(CrudDto.Filter),
                    fa.In.HasFlag(CrudDto.Sort)
                  );

            var apiName2 = fa?.ApiName ?? ToApiName(p.Name);
            var immutable = (fa?.Immutable ?? false) || string.Equals(p.Name, keyName, StringComparison.OrdinalIgnoreCase);
            var hardHidden = hidden || (fa?.Hidden ?? false);

            var (ft, nullable) = MapFieldType(p.PropertyType);

            var validation = new FieldValidationContract(
                RequiredOnCreate: fa?.RequiredOnCreate ?? false,
                MinLength: fa?.MinLength,
                MaxLength: fa?.MaxLength,
                Min: fa?.Min,
                Max: fa?.Max,
                Regex: fa?.Regex
            );

            fields.Add(new FieldContract(
                Name: p.Name,
                ApiName: apiName2,
                Type: ft,
                Nullable: nullable,
                InRead: inRead && !hardHidden,
                InCreate: inCreate && !hardHidden,
                InUpdate: inUpdate && !hardHidden && !immutable,
                Filterable: filterable && !hardHidden,
                Sortable: sortable && !hardHidden,
                Hidden: hardHidden,
                Immutable: immutable,
                Validation: validation
            ));
        }

        // Ensure key is present as a field in Read/filter/sort if annotated (or default)
        // (We don’t force it into Create/Update)
        if (!fields.Any(f => f.Name.Equals(keyName, StringComparison.OrdinalIgnoreCase)))
        {
            fields.Add(new FieldContract(
                Name: keyName,
                ApiName: ToApiName(keyName),
                Type: keyType,
                Nullable: false,
                InRead: true,
                InCreate: false,
                InUpdate: false,
                Filterable: true,
                Sortable: true,
                Hidden: false,
                Immutable: true,
                Validation: new FieldValidationContract(false, null, null, null, null, null)
            ));
        }

        // Query/read allowlists
        var filterableFields = fields.Where(f => f.Filterable).Select(f => f.ApiName).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var sortableFields = fields.Where(f => f.Sortable).Select(f => f.ApiName).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var expandAllowed = relations.Where(r => r.Read.ExpandAllowed).Select(r => r.ApiName).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var defaultExpand = relations.Where(r => r.Read.DefaultExpanded).Select(r => r.ApiName).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

        var query = new QueryContract(ra.MaxPageSize, filterableFields, sortableFields, DefaultSort: null);
        var read = new ReadContract(expandAllowed, ra.MaxExpandDepth, defaultExpand);

        // Operation shapes
        IReadOnlyList<string> readShape = fields.Where(f => f.InRead).Select(f => f.ApiName).ToList();
        IReadOnlyList<string> createShape = fields.Where(f => f.InCreate).Select(f => f.ApiName).ToList();
        IReadOnlyList<string> updateShape = fields.Where(f => f.InUpdate).Select(f => f.ApiName).ToList();

        // include relation expand objects in Get output only (actual expansion happens later)
        var getOutput = readShape.Concat(expandAllowed).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

        IReadOnlyList<string> requiredOnCreate = fields
            .Where(f => f.Validation.RequiredOnCreate)
            .Select(f => f.ApiName)
            .ToList();

        IReadOnlyList<string> immutableFields = fields
            .Where(f => f.Immutable)
            .Select(f => f.ApiName)
            .ToList();

        var ops = new Dictionary<CrudOperation, OperationContract>
        {
            [CrudOperation.List] = new(
                Enabled: ra.EnableList,
                InputShape: Array.Empty<string>(),
                OutputShape: readShape,
                RequiredOnCreate: Array.Empty<string>(),
                ImmutableFields: immutableFields,
                Concurrency: null),

            [CrudOperation.Get] = new(
                Enabled: ra.EnableGet,
                InputShape: Array.Empty<string>(),
                OutputShape: getOutput,
                RequiredOnCreate: Array.Empty<string>(),
                ImmutableFields: immutableFields,
                Concurrency: null),

            [CrudOperation.Create] = new(
                Enabled: ra.EnableCreate,
                InputShape: createShape,
                OutputShape: readShape,
                RequiredOnCreate: requiredOnCreate,
                ImmutableFields: immutableFields,
                Concurrency: null),

            [CrudOperation.Update] = new(
                Enabled: ra.EnableUpdate,
                InputShape: updateShape,
                OutputShape: readShape,
                RequiredOnCreate: Array.Empty<string>(),
                ImmutableFields: immutableFields,
                Concurrency: concurrency),

            [CrudOperation.Delete] = new(
                Enabled: ra.EnableDelete,
                InputShape: Array.Empty<string>(),
                OutputShape: Array.Empty<string>(),
                RequiredOnCreate: Array.Empty<string>(),
                ImmutableFields: immutableFields,
                Concurrency: null),
        };

        return new ResourceContract(
            ResourceKey: resourceKey,
            Route: ra.Route,
            Backend: ra.Backend,
            Key: key,
            Query: query,
            Read: read,
            Fields: fields,
            Relations: relations,
            Operations: ops,
            Security: new SecurityContract(policies)
        );
    }

    private RelationContract BuildRelation(PropertyInfo navProp, CrudRelationAttribute a, IDictionary<string, Type> keyMap)
    {
        var apiName = ToApiName(navProp.Name);

        var (targetType, isCollection) = GetNavigationTarget(navProp.PropertyType);

        var targetKey = targetType.Name; // default; must match [CrudResource.ResourceKey] or CLR name
        // if the target type has CrudResourceAttribute, prefer its ResourceKey
        var tra = targetType.GetCustomAttribute<CrudResourceAttribute>();
        if (tra != null && !string.IsNullOrWhiteSpace(tra.ResourceKey))
            targetKey = tra.ResourceKey!;
        else if (keyMap.ContainsKey(targetKey) == false)
            targetKey = targetType.Name;

        var kind = a.Kind ?? InferRelationKind(isCollection);

        var read = new RelationReadContract(a.ReadExpandAllowed, a.DefaultExpanded);

        var writeFieldName = a.WriteFieldName;
        var fkProp = a.ForeignKeyProperty;

        // Infer FK for many-to-one
        if (kind == RelationKind.ManyToOne)
        {
            fkProp ??= navProp.Name + "Id";
            writeFieldName ??= ToApiName(fkProp);
        }
        else if (kind == RelationKind.ManyToMany || kind == RelationKind.OneToMany)
        {
            writeFieldName ??= ToApiName(navProp.Name) + "Ids";
        }

        var write = new RelationWriteContract(a.WriteMode, writeFieldName, a.RequiredOnCreate, fkProp);

        return new RelationContract(
            Name: navProp.Name,
            ApiName: apiName,
            Kind: kind,
            TargetResourceKey: targetKey,
            Read: read,
            Write: write
        );
    }

    private static RelationKind InferRelationKind(bool isCollection)
        => isCollection ? RelationKind.OneToMany : RelationKind.ManyToOne;

    private static (string KeyName, FieldType KeyType) DiscoverKey(Type t, string? overrideKey)
    {
        if (!string.IsNullOrWhiteSpace(overrideKey))
        {
            var p = t.GetProperty(overrideKey!, BindingFlags.Public | BindingFlags.Instance)
                ?? throw new ContractValidationException(new[] { $"KeyProperty '{overrideKey}' not found on {t.Name}." });
            var (ft, _) = MapFieldType(p.PropertyType);
            return (p.Name, ft);
        }

        // common patterns: Id, {TypeName}Id
        var id = t.GetProperty("Id") ?? t.GetProperty(t.Name + "Id");
        if (id == null)
            throw new ContractValidationException(new[] { $"No key property found on {t.Name}. Add 'Id' or set CrudResourceAttribute.KeyProperty." });

        var (ft2, _) = MapFieldType(id.PropertyType);
        return (id.Name, ft2);
    }

    private static (bool inRead, bool inCreate, bool inUpdate, bool filterable, bool sortable) DefaultScalarMembership()
        => (inRead: true, inCreate: false, inUpdate: false, filterable: false, sortable: false);

    private static string ToApiName(string clrName)
    {
        // simple camelCase
        if (string.IsNullOrEmpty(clrName)) return clrName;
        return char.ToLowerInvariant(clrName[0]) + clrName[1..];
    }

    private static bool IsNavigationProperty(PropertyInfo p)
    {
        if (p.PropertyType == typeof(string)) return false;
        if (IsScalar(p.PropertyType)) return false;

        // collections are navigations; complex types are navigations by default here
        return true;
    }

    private static (Type Target, bool IsCollection) GetNavigationTarget(Type t)
    {
        if (t == typeof(string)) return (t, false);

        if (t.IsGenericType && typeof(System.Collections.IEnumerable).IsAssignableFrom(t))
        {
            var arg = t.GetGenericArguments().FirstOrDefault() ?? typeof(object);
            return (arg, true);
        }

        return (t, false);
    }

    private static bool IsScalar(Type t)
    {
        t = Nullable.GetUnderlyingType(t) ?? t;
        if (t.IsEnum) return true;

        return t == typeof(string)
            || t == typeof(int) || t == typeof(long)
            || t == typeof(decimal)
            || t == typeof(bool)
            || t == typeof(DateTime)
            || t == typeof(Guid)
            || t == typeof(double) || t == typeof(float)
            || t == typeof(byte[]) // treat as scalar
            ;
    }

    private static (FieldType Type, bool Nullable) MapFieldType(Type t)
    {
        var nullable = false;
        var ut = Nullable.GetUnderlyingType(t);
        if (ut != null) { nullable = true; t = ut; }

        if (t == typeof(string)) return (FieldType.String, true);
        if (t == typeof(int)) return (FieldType.Int32, nullable);
        if (t == typeof(long)) return (FieldType.Int64, nullable);
        if (t == typeof(decimal)) return (FieldType.Decimal, nullable);
        if (t == typeof(bool)) return (FieldType.Boolean, nullable);
        if (t == typeof(DateTime)) return (FieldType.DateTime, nullable);
        if (t == typeof(Guid)) return (FieldType.Guid, nullable);

        if (t.IsEnum) return (FieldType.Enum, nullable);

        // arrays (common)
        if (t == typeof(string[])) return (FieldType.StringArray, true);
        if (t == typeof(int[])) return (FieldType.IntArray, true);
        if (t == typeof(Guid[])) return (FieldType.GuidArray, true);
        if (t == typeof(decimal[])) return (FieldType.DecimalArray, true);

        // fallback: Json
        return (FieldType.Json, true);
    }

    private static void ValidateAll(IReadOnlyList<ResourceContract> contracts)
    {
        var errors = new List<string>();

        // route uniqueness
        var dupRoutes = contracts.GroupBy(c => c.Route, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToList();

        foreach (var r in dupRoutes)
            errors.Add($"Duplicate route '{r}' across resources.");

        foreach (var c in contracts)
        {
            // field apiName uniqueness
            var dupFieldNames = c.Fields.GroupBy(f => f.ApiName, StringComparer.OrdinalIgnoreCase)
                .Where(g => g.Count() > 1)
                .Select(g => g.Key);

            foreach (var f in dupFieldNames)
                errors.Add($"Resource '{c.ResourceKey}' has duplicate field apiName '{f}'.");

            // key exists
            if (c.Fields.All(f => !f.Name.Equals(c.Key.Name, StringComparison.OrdinalIgnoreCase)))
                errors.Add($"Resource '{c.ResourceKey}' key '{c.Key.Name}' is not present in Fields.");

            // expand depth sanity
            if (c.Read.MaxExpandDepth < 0 || c.Read.MaxExpandDepth > 3)
                errors.Add($"Resource '{c.ResourceKey}' MaxExpandDepth {c.Read.MaxExpandDepth} looks unsafe (recommended 0..3).");

            // operation shapes reference known fields/relations
            var known = new HashSet<string>(c.Fields.Select(f => f.ApiName), StringComparer.OrdinalIgnoreCase);
            foreach (var rel in c.Relations.Where(r => r.Read.ExpandAllowed))
                known.Add(rel.ApiName);

            foreach (var op in c.Operations)
            {
                foreach (var n in op.Value.InputShape.Concat(op.Value.OutputShape))
                {
                    if (!known.Contains(n))
                        errors.Add($"Resource '{c.ResourceKey}' op '{op.Key}' references unknown field/relation '{n}'.");
                }
            }
        }

        if (errors.Count > 0)
            throw new ContractValidationException(errors);
    }
}
