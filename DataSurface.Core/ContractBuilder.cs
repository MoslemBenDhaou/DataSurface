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
    /// <exception cref="ContractValidationException">Thrown when the generated contracts fail validation.</exception>
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
    /// <exception cref="ContractValidationException">Thrown when the generated contracts fail validation.</exception>
    public IReadOnlyList<ResourceContract> BuildFromTypes(params Type[] resourceTypes)
    {
        var errors = new List<string>();

        // First pass: create resource stubs to resolve targets by resourceKey
        var resourceAttrs = new List<(Type Type, CrudResourceAttribute Attr)>();
        foreach (var t in resourceTypes)
        {
            var attr = t.GetCustomAttribute<CrudResourceAttribute>();
            if (attr is null)
            {
                errors.Add($"Type '{t.FullName}' was passed to BuildFromTypes but is missing [CrudResource].");
                continue;
            }
            resourceAttrs.Add((t, attr));
        }

        var keyMap = new Dictionary<string, Type>(StringComparer.OrdinalIgnoreCase);
        foreach (var (type, attr) in resourceAttrs)
        {
            var rk = attr.ResourceKey ?? type.Name;
            if (!keyMap.TryAdd(rk, type))
                errors.Add($"Duplicate resource key '{rk}' used by '{keyMap[rk].FullName}' and '{type.FullName}'. Set CrudResourceAttribute.ResourceKey to disambiguate.");
        }

        if (errors.Count > 0)
            throw new ContractValidationException(errors);

        var contracts = resourceAttrs.Select(x => BuildSingle(x.Type, x.Attr, keyMap, errors)).ToList();

        ValidateAll(contracts, errors);

        return contracts;
    }

    private ResourceContract BuildSingle(Type clrType, CrudResourceAttribute ra, IDictionary<string, Type> keyMap, List<string> errors)
    {
        var resourceKey = ra.ResourceKey ?? clrType.Name;

        var (keyName, keyType, keyApiOverride) = DiscoverKey(clrType, ra.KeyProperty);
        var key = new ResourceKeyContract(keyName, keyType);

        // security policies: opt-in via [CrudAuthorize] attribute
        // By default, no authorization is required - policies are only set when explicitly configured.
        // Global (no-operation) attributes are applied first so per-operation attributes always win,
        // regardless of reflection's attribute enumeration order.
        var policies = new Dictionary<CrudOperation, string?>();
        var authAttrs = clrType.GetCustomAttributes<CrudAuthorizeAttribute>().ToList();

        foreach (var auth in authAttrs.Where(a => !a.HasOperation))
        {
            policies[CrudOperation.List] = auth.Policy;
            policies[CrudOperation.Get] = auth.Policy;
            policies[CrudOperation.Create] = auth.Policy;
            policies[CrudOperation.Update] = auth.Policy;
            policies[CrudOperation.Delete] = auth.Policy;
        }

        foreach (var auth in authAttrs.Where(a => a.HasOperation))
            policies[auth.Operation] = auth.Policy;

        // Fields + relations
        var fields = new List<FieldContract>();
        var relations = new List<RelationContract>();

        PropertyInfo? concurrencyProp = null;
        CrudConcurrencyAttribute? concurrencyAttr = null;
        PropertyInfo? tenantProp = null;
        TenantContract? tenant = null;

        foreach (var p in clrType.GetProperties(BindingFlags.Instance | BindingFlags.Public))
        {
            if (p.GetIndexParameters().Length > 0) continue;

            if (p.GetCustomAttribute<CrudIgnoreAttribute>() != null)
            {
                if (p.GetCustomAttribute<CrudFieldAttribute>() != null)
                    errors.Add($"Resource '{resourceKey}': property '{p.Name}' has both [CrudIgnore] and [CrudField]; remove one.");
                continue;
            }

            // Concurrency field
            var cc = p.GetCustomAttribute<CrudConcurrencyAttribute>();
            if (cc != null)
            {
                if (concurrencyProp != null)
                    errors.Add($"Resource '{resourceKey}': multiple [CrudConcurrency] properties ('{concurrencyProp.Name}', '{p.Name}'); only one is allowed.");
                concurrencyProp = p;
                concurrencyAttr = cc;
                // still treat it as a field if annotated or default-included
            }

            // Tenant field
            var ta = p.GetCustomAttribute<CrudTenantAttribute>();
            if (ta != null)
            {
                if (tenantProp != null)
                    errors.Add($"Resource '{resourceKey}': multiple [CrudTenant] properties ('{tenantProp.Name}', '{p.Name}'); only one is allowed.");
                tenantProp = p;
                tenant = new TenantContract(p.Name, ToApiName(p.Name), ta.ClaimType, ta.Required);
            }

            if (IsNavigationProperty(p))
            {
                var relAttr = p.GetCustomAttribute<CrudRelationAttribute>();
                if (relAttr == null)
                {
                    // Safe default: relations are opt-in. But a [CrudField] on a navigation-shaped
                    // property is a declaration we cannot honor — fail loudly instead of dropping it.
                    if (p.GetCustomAttribute<CrudFieldAttribute>() != null)
                        errors.Add($"Resource '{resourceKey}': property '{p.Name}' of type '{p.PropertyType.Name}' has [CrudField] but is not a supported scalar type. Use [CrudRelation] for navigations, or remove the attribute.");
                    continue;
                }
                relations.Add(BuildRelation(p, relAttr, keyMap, resourceKey, errors));
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

            var isKeyProp = string.Equals(p.Name, keyName, StringComparison.OrdinalIgnoreCase);
            var apiName2 = fa?.ApiName ?? (isKeyProp ? keyApiOverride : null) ?? ToApiName(p.Name);
            var immutable = (fa?.Immutable ?? false) || isKeyProp;
            var hardHidden = hidden || (fa?.Hidden ?? false);

            var (ft, nullable) = MapFieldType(p.PropertyType);

            var searchable = fa?.Searchable ?? false;
            var computed = !string.IsNullOrWhiteSpace(fa?.ComputedExpression);
            var computedExpr = fa?.ComputedExpression;
            var defaultValue = fa?.DefaultValue;
            var allowedValues = !string.IsNullOrWhiteSpace(fa?.AllowedValues)
                ? fa!.AllowedValues.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList()
                : null;

            var validation = new FieldValidationContract(
                RequiredOnCreate: fa?.RequiredOnCreate ?? false,
                MinLength: fa?.MinLength,
                MaxLength: fa?.MaxLength,
                Min: fa?.Min,
                Max: fa?.Max,
                Regex: fa?.Regex,
                AllowedValues: allowedValues
            );

            fields.Add(new FieldContract(
                Name: p.Name,
                ApiName: apiName2,
                Type: ft,
                Nullable: nullable,
                InRead: inRead && !hardHidden,
                InCreate: inCreate && !hardHidden && !computed,
                InUpdate: inUpdate && !hardHidden && !immutable && !computed,
                Filterable: filterable && !hardHidden,
                Sortable: sortable && !hardHidden,
                Hidden: hardHidden,
                Immutable: immutable || computed,
                Searchable: searchable && !hardHidden,
                Computed: computed,
                ComputedExpression: computedExpr,
                DefaultValue: defaultValue,
                Validation: validation
            ));
        }

        // The tenant discriminator is server-managed: never client-writable, regardless of
        // [CrudField] flags. Allowing client writes would permit cross-tenant reassignment.
        if (tenantProp != null)
        {
            for (var i = 0; i < fields.Count; i++)
            {
                if (string.Equals(fields[i].Name, tenantProp.Name, StringComparison.OrdinalIgnoreCase) &&
                    (fields[i].InCreate || fields[i].InUpdate || !fields[i].Immutable))
                {
                    fields[i] = fields[i] with { InCreate = false, InUpdate = false, Immutable = true };
                }
            }
        }

        // Concurrency contract: the token's api name must match the field's actual api name
        // (a [CrudField(ApiName = ...)] override included), and clients must be able to read
        // the token back or updates can never satisfy RequiredOnUpdate.
        ConcurrencyContract? concurrency = null;
        if (concurrencyProp != null && concurrencyAttr != null)
        {
            var tokenField = fields.FirstOrDefault(f => string.Equals(f.Name, concurrencyProp.Name, StringComparison.OrdinalIgnoreCase));
            var tokenApiName = tokenField?.ApiName ?? ToApiName(concurrencyProp.Name);
            concurrency = new ConcurrencyContract(concurrencyAttr.Mode, tokenApiName, concurrencyAttr.RequiredOnUpdate);

            if (tokenField == null)
            {
                // Auto-expose the token read-only so clients can obtain it.
                var (tft, _) = MapFieldType(concurrencyProp.PropertyType);
                fields.Add(new FieldContract(
                    Name: concurrencyProp.Name,
                    ApiName: tokenApiName,
                    Type: tft,
                    Nullable: true,
                    InRead: true,
                    InCreate: false,
                    InUpdate: false,
                    Filterable: false,
                    Sortable: false,
                    Hidden: false,
                    Immutable: true,
                    Searchable: false,
                    Computed: false,
                    ComputedExpression: null,
                    DefaultValue: null,
                    Validation: new FieldValidationContract(false, null, null, null, null, null)
                ));
            }
            else if (!tokenField.InRead && concurrencyAttr.RequiredOnUpdate)
            {
                errors.Add($"Resource '{resourceKey}': concurrency token '{concurrencyProp.Name}' is required on update but not readable (InRead is false); clients can never obtain the token.");
            }
        }

        // Ensure key is present as a field in Read/filter/sort if annotated (or default)
        // (We don’t force it into Create/Update)
        if (!fields.Any(f => f.Name.Equals(keyName, StringComparison.OrdinalIgnoreCase)))
        {
            fields.Add(new FieldContract(
                Name: keyName,
                ApiName: keyApiOverride ?? ToApiName(keyName),
                Type: keyType,
                Nullable: false,
                InRead: true,
                InCreate: false,
                InUpdate: false,
                Filterable: true,
                Sortable: true,
                Hidden: false,
                Immutable: true,
                Searchable: false,
                Computed: false,
                ComputedExpression: null,
                DefaultValue: null,
                Validation: new FieldValidationContract(false, null, null, null, null, null)
            ));
        }

        // Query/read allowlists
        var filterableFields = fields.Where(f => f.Filterable).Select(f => f.ApiName).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var sortableFields = fields.Where(f => f.Sortable).Select(f => f.ApiName).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var expandAllowed = relations.Where(r => r.Read.ExpandAllowed).Select(r => r.ApiName).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var defaultExpand = relations.Where(r => r.Read.DefaultExpanded).Select(r => r.ApiName).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

        var searchableFields = fields.Where(f => f.Searchable).Select(f => f.ApiName).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

        var query = new QueryContract(ra.MaxPageSize, filterableFields, sortableFields, searchableFields, DefaultSort: ra.DefaultSort);
        var read = new ReadContract(expandAllowed, ra.MaxExpandDepth, defaultExpand);

        // Operation shapes
        IReadOnlyList<string> readShape = fields.Where(f => f.InRead).Select(f => f.ApiName).ToList();
        IReadOnlyList<string> createShape = fields.Where(f => f.InCreate).Select(f => f.ApiName).ToList();
        IReadOnlyList<string> updateShape = fields.Where(f => f.InUpdate).Select(f => f.ApiName).ToList();

        // include relation expand objects in read outputs (actual expansion happens later);
        // both List and Get support ?expand, so both shapes include the expandable relations
        var readOutput = readShape.Concat(expandAllowed).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

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
                OutputShape: readOutput,
                RequiredOnCreate: Array.Empty<string>(),
                ImmutableFields: immutableFields,
                Concurrency: null),

            [CrudOperation.Get] = new(
                Enabled: ra.EnableGet,
                InputShape: Array.Empty<string>(),
                OutputShape: readOutput,
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
            Security: new SecurityContract(policies),
            Tenant: tenant
        );
    }

    private RelationContract BuildRelation(PropertyInfo navProp, CrudRelationAttribute a, IDictionary<string, Type> keyMap, string resourceKey, List<string> errors)
    {
        var apiName = ToApiName(navProp.Name);

        var (targetType, isCollection) = GetNavigationTarget(navProp.PropertyType);

        var targetKey = targetType.Name; // default; must match [CrudResource.ResourceKey] or CLR name
        // if the target type has CrudResourceAttribute, prefer its ResourceKey
        var tra = targetType.GetCustomAttribute<CrudResourceAttribute>();
        if (tra != null && !string.IsNullOrWhiteSpace(tra.ResourceKey))
            targetKey = tra.ResourceKey!;
        else if (tra == null && !keyMap.ContainsKey(targetKey))
            errors.Add($"Resource '{resourceKey}': relation '{navProp.Name}' targets type '{targetType.FullName}', which is not a [CrudResource] and matches no registered resource key.");

        var kind = a.Kind ?? InferRelationKind(isCollection);

        var read = new RelationReadContract(a.ReadExpandAllowed, a.DefaultExpanded);

        if (a.DefaultExpanded && !a.ReadExpandAllowed)
            errors.Add($"Resource '{resourceKey}': relation '{navProp.Name}' has DefaultExpanded = true but ReadExpandAllowed = false; the default expansion would never happen.");

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

    private static (string KeyName, FieldType KeyType, string? KeyApiName) DiscoverKey(Type t, string? overrideKey)
    {
        var props = t.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.GetIndexParameters().Length == 0)
            .ToArray();

        if (!string.IsNullOrWhiteSpace(overrideKey))
        {
            var p = props.FirstOrDefault(x => x.Name.Equals(overrideKey, StringComparison.OrdinalIgnoreCase))
                ?? throw new ContractValidationException(new[] { $"KeyProperty '{overrideKey}' not found on {t.Name}." });
            var (ft, _) = MapFieldType(p.PropertyType);
            return (p.Name, ft, p.GetCustomAttribute<CrudKeyAttribute>()?.ApiName);
        }

        var crudKeys = props.Where(x => x.GetCustomAttribute<CrudKeyAttribute>() != null).ToArray();
        if (crudKeys.Length > 1)
            throw new ContractValidationException(new[] { $"Multiple [CrudKey] properties on {t.Name} ({string.Join(", ", crudKeys.Select(k => k.Name))}); only one is allowed." });

        // [CrudKey] wins; common patterns Id / {TypeName}Id are fallbacks (case-insensitive)
        var id = crudKeys.FirstOrDefault()
            ?? props.FirstOrDefault(x => x.Name.Equals("Id", StringComparison.OrdinalIgnoreCase))
            ?? props.FirstOrDefault(x => x.Name.Equals(t.Name + "Id", StringComparison.OrdinalIgnoreCase));

        if (id == null)
            throw new ContractValidationException(new[] { $"No key property found on {t.Name}. Add [CrudKey], an 'Id' or '{t.Name}Id' property, or set CrudResourceAttribute.KeyProperty." });

        var (ft2, _) = MapFieldType(id.PropertyType);
        return (id.Name, ft2, id.GetCustomAttribute<CrudKeyAttribute>()?.ApiName);
    }

    private (bool inRead, bool inCreate, bool inUpdate, bool filterable, bool sortable) DefaultScalarMembership()
        => (inRead: _opt.DefaultIncludeScalarsInRead, inCreate: false, inUpdate: false, filterable: false, sortable: false);

    private string ToApiName(string clrName)
    {
        if (string.IsNullOrEmpty(clrName)) return clrName;
        if (!_opt.UseCamelCaseApiNames) return clrName;
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

        if (t.IsArray)
            return (t.GetElementType()!, true);

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
            || t == typeof(short) || t == typeof(byte) || t == typeof(sbyte)
            || t == typeof(ushort) || t == typeof(uint) || t == typeof(ulong)
            || t == typeof(decimal)
            || t == typeof(bool)
            || t == typeof(char)
            || t == typeof(DateTime) || t == typeof(DateTimeOffset)
            || t == typeof(DateOnly) || t == typeof(TimeOnly) || t == typeof(TimeSpan)
            || t == typeof(Guid)
            || t == typeof(double) || t == typeof(float)
            || t == typeof(byte[]) // treat as scalar
            || t == typeof(string[]) || t == typeof(int[]) || t == typeof(Guid[]) || t == typeof(decimal[])
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
        if (t == typeof(short) || t == typeof(byte) || t == typeof(sbyte) || t == typeof(ushort)) return (FieldType.Int32, nullable);
        if (t == typeof(uint) || t == typeof(ulong)) return (FieldType.Int64, nullable);
        if (t == typeof(decimal)) return (FieldType.Decimal, nullable);
        if (t == typeof(bool)) return (FieldType.Boolean, nullable);
        if (t == typeof(DateTime) || t == typeof(DateTimeOffset) || t == typeof(DateOnly)) return (FieldType.DateTime, nullable);
        if (t == typeof(TimeOnly) || t == typeof(TimeSpan)) return (FieldType.String, nullable);
        if (t == typeof(char)) return (FieldType.String, nullable);
        if (t == typeof(Guid)) return (FieldType.Guid, nullable);
        if (t == typeof(double)) return (FieldType.Decimal, nullable);
        if (t == typeof(float)) return (FieldType.Decimal, nullable);

        if (t.IsEnum) return (FieldType.Enum, nullable);

        // byte[] tokens (rowversion) are exchanged as base64 strings on the wire
        if (t == typeof(byte[])) return (FieldType.String, true);

        // arrays (common)
        if (t == typeof(string[])) return (FieldType.StringArray, true);
        if (t == typeof(int[])) return (FieldType.IntArray, true);
        if (t == typeof(Guid[])) return (FieldType.GuidArray, true);
        if (t == typeof(decimal[])) return (FieldType.DecimalArray, true);

        // fallback: Json
        return (FieldType.Json, true);
    }

    private static void ValidateAll(IReadOnlyList<ResourceContract> contracts, List<string> errors)
    {
        // route uniqueness
        var dupRoutes = contracts.GroupBy(c => c.Route, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToList();

        foreach (var r in dupRoutes)
            errors.Add($"Duplicate route '{r}' across resources.");

        var resourceKeys = new HashSet<string>(contracts.Select(c => c.ResourceKey), StringComparer.OrdinalIgnoreCase);

        foreach (var c in contracts)
        {
            // field apiName uniqueness
            var dupFieldNames = c.Fields.GroupBy(f => f.ApiName, StringComparer.OrdinalIgnoreCase)
                .Where(g => g.Count() > 1)
                .Select(g => g.Key);

            foreach (var f in dupFieldNames)
                errors.Add($"Resource '{c.ResourceKey}' has duplicate field apiName '{f}'.");

            // relation apiNames must not collide with field apiNames
            var fieldApiNames = new HashSet<string>(c.Fields.Select(f => f.ApiName), StringComparer.OrdinalIgnoreCase);
            foreach (var rel in c.Relations)
            {
                if (fieldApiNames.Contains(rel.ApiName))
                    errors.Add($"Resource '{c.ResourceKey}' relation '{rel.ApiName}' collides with a field of the same apiName.");
            }

            // key exists
            if (c.Fields.All(f => !f.Name.Equals(c.Key.Name, StringComparison.OrdinalIgnoreCase)))
                errors.Add($"Resource '{c.ResourceKey}' key '{c.Key.Name}' is not present in Fields.");

            // expand depth sanity (hard limit)
            if (c.Read.MaxExpandDepth < 0 || c.Read.MaxExpandDepth > 3)
                errors.Add($"Resource '{c.ResourceKey}' MaxExpandDepth {c.Read.MaxExpandDepth} is out of the supported range 0..3.");

            // expandable relations must target a resource in this contract set
            foreach (var rel in c.Relations.Where(r => r.Read.ExpandAllowed || r.Read.DefaultExpanded))
            {
                if (!resourceKeys.Contains(rel.TargetResourceKey))
                    errors.Add($"Resource '{c.ResourceKey}' relation '{rel.ApiName}' targets unknown resource '{rel.TargetResourceKey}'; expansion would fail at runtime.");
            }

            // default sort must reference sortable fields
            if (!string.IsNullOrWhiteSpace(c.Query.DefaultSort))
            {
                var sortable = new HashSet<string>(c.Query.SortableFields, StringComparer.OrdinalIgnoreCase);
                foreach (var seg in c.Query.DefaultSort.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                {
                    var fieldName = seg.StartsWith('-') ? seg[1..] : seg;
                    if (!sortable.Contains(fieldName))
                        errors.Add($"Resource '{c.ResourceKey}' DefaultSort references '{fieldName}', which is not a sortable field.");
                }
            }

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
