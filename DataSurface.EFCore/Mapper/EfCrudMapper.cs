using System.Collections;
using System.Text.Json;
using System.Text.Json.Nodes;
using DataSurface.Core;
using DataSurface.Core.Contracts;
using DataSurface.Core.Enums;
using DataSurface.EFCore.Exceptions;
using DataSurface.EFCore.Interfaces;
using DataSurface.EFCore.Services;
using Microsoft.EntityFrameworkCore;

namespace DataSurface.EFCore.Mapper;

/// <summary>
/// Maps JSON request payloads onto EF Core entities using a <see cref="ResourceContract"/>.
/// </summary>
/// <remarks>
/// This mapper is designed to apply contract-defined allowlists:
/// - fields are mapped based on the operation input shape
/// - immutable/hidden fields are skipped
/// - relation writes support FK assignment and collection replacement by IDs
/// - update operations may apply optimistic concurrency based on the contract concurrency settings
/// </remarks>
public sealed class EfCrudMapper
{
    private readonly JsonSerializerOptions _json;
    private readonly DataSurfaceFeatures _features;
    private readonly CrudSecurityDispatcher? _security;
    private readonly IResourceContractProvider? _contracts;

    /// <summary>
    /// Creates a new mapper instance.
    /// </summary>
    /// <param name="features">Feature flags; default values are only applied when <see cref="DataSurfaceFeatures.EnableDefaultValues"/> is enabled.</param>
    /// <param name="security">Optional security dispatcher; when present, relation writes load target entities through the same tenant/row-level scoping as reads.</param>
    /// <param name="contracts">Optional contract provider used to resolve relation-target contracts for scoped relation writes.</param>
    public EfCrudMapper(DataSurfaceFeatures? features = null, CrudSecurityDispatcher? security = null, IResourceContractProvider? contracts = null)
    {
        _features = features ?? new DataSurfaceFeatures();
        _security = security;
        _contracts = contracts;
        _json = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        };
    }

    /// <summary>
    /// Creates a new entity instance and applies create semantics from the given JSON <paramref name="body"/>.
    /// </summary>
    /// <typeparam name="TEntity">Entity CLR type.</typeparam>
    /// <param name="body">JSON request body.</param>
    /// <param name="contract">Resource contract describing allowed fields and relation writes.</param>
    /// <param name="db">EF Core DbContext used for relation resolution.</param>
    /// <returns>A new entity populated from the request body.</returns>
    public TEntity CreateEntity<TEntity>(
        JsonObject body,
        ResourceContract contract,
        DbContext db)
        where TEntity : class, new()
    {
        var entity = new TEntity();
        ApplyDefaultValues(entity, body, contract);
        ApplyFields(entity, body, contract, CrudOperation.Create, db);
        ApplyRelationWrites(entity, body, contract, CrudOperation.Create, db);
        return entity;
    }

    private void ApplyDefaultValues<TEntity>(TEntity entity, JsonObject body, ResourceContract contract)
        where TEntity : class
    {
        if (!_features.EnableDefaultValues) return;

        foreach (var field in contract.Fields)
        {
            if (field.DefaultValue is null) continue;
            if (field.Hidden || !field.InCreate) continue;

            // Only apply default if field not provided in body (case-insensitive, like ApplyFields)
            if (body.Any(kv => string.Equals(kv.Key, field.ApiName, StringComparison.OrdinalIgnoreCase))) continue;

            // Use entity.GetType() instead of typeof(TEntity) to get actual runtime type
            var prop = entity.GetType().GetProperty(field.Name);
            if (prop == null || !prop.CanWrite) continue;

            try
            {
                var defaultVal = ConvertDefaultValue(field.DefaultValue, prop.PropertyType);
                prop.SetValue(entity, defaultVal);
            }
            catch { /* Skip if conversion fails */ }
        }
    }

    private static object? ConvertDefaultValue(object defaultValue, Type targetType)
    {
        if (defaultValue is null) return null;

        var nonNullable = Nullable.GetUnderlyingType(targetType) ?? targetType;

        if (nonNullable == defaultValue.GetType())
            return defaultValue;

        if (nonNullable == typeof(string))
            return defaultValue.ToString();

        if (nonNullable == typeof(int))
            return Convert.ToInt32(defaultValue);

        if (nonNullable == typeof(long))
            return Convert.ToInt64(defaultValue);

        if (nonNullable == typeof(decimal))
            return Convert.ToDecimal(defaultValue);

        if (nonNullable == typeof(bool))
            return Convert.ToBoolean(defaultValue);

        if (nonNullable == typeof(DateTime))
            return DateTime.Parse(defaultValue.ToString()!);

        if (nonNullable == typeof(Guid))
            return Guid.Parse(defaultValue.ToString()!);

        if (nonNullable.IsEnum)
            return Enum.Parse(nonNullable, defaultValue.ToString()!, ignoreCase: true);

        return Convert.ChangeType(defaultValue, nonNullable);
    }

    /// <summary>
    /// Applies update semantics to an existing entity from the given JSON <paramref name="body"/>.
    /// </summary>
    /// <typeparam name="TEntity">Entity CLR type.</typeparam>
    /// <param name="entity">Existing entity instance to mutate.</param>
    /// <param name="body">JSON request body.</param>
    /// <param name="contract">Resource contract describing allowed fields, relations, and concurrency.</param>
    /// <param name="db">EF Core DbContext used for relation resolution and concurrency tracking.</param>
    public void ApplyUpdate<TEntity>(
        TEntity entity,
        JsonObject body,
        ResourceContract contract,
        DbContext db)
        where TEntity : class
    {
        ApplyConcurrency(entity, body, contract, db);
        ApplyFields(entity, body, contract, CrudOperation.Update, db, patchLike: true);
        ApplyRelationWrites(entity, body, contract, CrudOperation.Update, db);
    }

    private void ApplyFields<TEntity>(
        TEntity entity,
        JsonObject body,
        ResourceContract contract,
        CrudOperation op,
        DbContext db,
        bool patchLike = false)
        where TEntity : class
    {
        var allowed = contract.Operations[op].InputShape;
        var allow = new HashSet<string>(allowed, StringComparer.OrdinalIgnoreCase);

        foreach (var kv in body)
        {
            var apiName = kv.Key;
            if (!allow.Contains(apiName))
                continue; // Phase 3 can enforce "unknown field => 400"; mapper can be permissive.

            var field = contract.Fields.FirstOrDefault(f => f.ApiName.Equals(apiName, StringComparison.OrdinalIgnoreCase));
            if (field == null || field.Hidden) continue;

            if (op == CrudOperation.Update && field.Immutable) continue;

            // Use entity.GetType() instead of typeof(TEntity) to get actual runtime type
            var prop = entity.GetType().GetProperty(field.Name);
            if (prop == null || !prop.CanWrite) continue;

            // PATCH-like: only apply provided keys (body enumerates only provided anyway)
            // convert JsonNode into target type
            var value = DeserializeNode(kv.Value, prop.PropertyType);
            prop.SetValue(entity, value);
        }

        // For Create: check required fields (mapper doesn’t throw yet; Phase 3 validator will)
        // But you can enforce here if you want strict behavior.
    }

    private void ApplyConcurrency<TEntity>(
        TEntity entity,
        JsonObject body,
        ResourceContract contract,
        DbContext db)
        where TEntity : class
    {
        var cc = contract.Operations[CrudOperation.Update].Concurrency;
        if (cc == null || cc.Mode == ConcurrencyMode.None) return;

        // Missing token is rejected earlier by ValidateBody when RequiredOnUpdate is set;
        // nothing to apply here either way.
        if (!body.TryGetPropertyValue(cc.FieldApiName, out var tokenNode))
            return;

        // Typical RowVersion: API sends base64 string
        if (cc.Mode == ConcurrencyMode.RowVersion)
        {
            var tokenStr = tokenNode?.GetValue<string>();
            if (string.IsNullOrWhiteSpace(tokenStr)) return;

            var bytes = Convert.FromBase64String(tokenStr);

            // Set ORIGINAL value for concurrency check
            var entry = db.Entry(entity);
            // Look up the CLR property name from the contract by matching apiName
            var concurrencyField = contract.Fields.FirstOrDefault(f =>
                f.ApiName.Equals(cc.FieldApiName, StringComparison.OrdinalIgnoreCase));
            var propName = concurrencyField?.Name ?? "RowVersion";
            
            // Check if property exists on the actual entity type
            var concurrencyProp = entity.GetType().GetProperty(propName);
            if (concurrencyProp == null) return;
            
            entry.Property(propName).OriginalValue = bytes;
        }
    }

    private void ApplyRelationWrites<TEntity>(
        TEntity entity,
        JsonObject body,
        ResourceContract contract,
        CrudOperation op,
        DbContext db)
        where TEntity : class
    {
        foreach (var rel in contract.Relations)
        {
            var write = rel.Write;
            if (write.Mode == RelationWriteMode.None || write.Mode == RelationWriteMode.NestedDisabled)
                continue;

            var keyName = write.WriteFieldName;
            if (string.IsNullOrWhiteSpace(keyName)) continue;

            if (!body.TryGetPropertyValue(keyName!, out var node) || node is null)
                continue;

            var targetContract = ResolveContractOrNull(rel.TargetResourceKey);

            if (write.Mode == RelationWriteMode.ById)
            {
                // Set FK property (e.g. UserId)
                if (string.IsNullOrWhiteSpace(write.ForeignKeyProperty)) continue;

                var fkProp = entity.GetType().GetProperty(write.ForeignKeyProperty!);
                if (fkProp == null || !fkProp.CanWrite) continue;

                var fkVal = DeserializeNode(node, fkProp.PropertyType);

                // The FK target must be visible to the caller (tenant / row-level / soft-delete
                // scoped). Without this, a caller could attach another tenant's row by id.
                if (fkVal is not null)
                {
                    var navTargetType = GetNavigationTarget(entity.GetType().GetProperty(rel.Name)?.PropertyType ?? typeof(object)).Target;
                    var resolvedTarget = navTargetType != typeof(object) ? navTargetType : null;
                    if (resolvedTarget is not null && !TargetExistsInScope(db, resolvedTarget, targetContract, fkVal))
                    {
                        throw new CrudRequestValidationException(new Dictionary<string, string[]>
                        {
                            [keyName!] = new[] { "Related entity does not exist or is not accessible." }
                        });
                    }
                }

                fkProp.SetValue(entity, fkVal);
            }
            else if (write.Mode == RelationWriteMode.ByIdList)
            {
                // Replace a collection navigation with loaded entities by IDs
                var navProp = entity.GetType().GetProperty(rel.Name);
                if (navProp == null) continue;

                var (targetType, isCollection) = GetNavigationTarget(navProp.PropertyType);
                if (!isCollection) continue;

                var ids = DeserializeIds(node, targetType, targetContract);
                ReplaceCollectionByIds(entity, navProp, targetType, targetContract, keyName!, ids, db);
            }
        }
    }

    private ResourceContract? ResolveContractOrNull(string resourceKey)
        => _contracts?.All.FirstOrDefault(rc => rc.ResourceKey.Equals(resourceKey, StringComparison.OrdinalIgnoreCase));

    // Composes the same visibility scoping the CRUD service applies to reads: row-level
    // security filters, tenant isolation, and the soft-delete predicate. Relation writes
    // must load their targets through this scope or they become a cross-tenant primitive.
    private IQueryable ScopedTargetQuery(DbContext db, Type targetType, ResourceContract? targetContract)
    {
        var set = GetDbSet(db, targetType);

        if (_security is not null && targetContract is not null)
        {
            set = _security.ApplyResourceFilter(set, targetType, targetContract);
            if (targetContract.Tenant is not null)
                set = _security.ApplyTenantFilter(set, targetType, targetContract);
        }

        if (typeof(ISoftDelete).IsAssignableFrom(targetType))
        {
            var p = System.Linq.Expressions.Expression.Parameter(targetType, "t");
            var notDeleted = System.Linq.Expressions.Expression.Not(
                System.Linq.Expressions.Expression.Property(p, nameof(ISoftDelete.IsDeleted)));
            var lambda = System.Linq.Expressions.Expression.Lambda(notDeleted, p);
            var whereMethod = typeof(Queryable).GetMethods()
                .First(m => m.Name == nameof(Queryable.Where) && m.GetParameters().Length == 2)
                .MakeGenericMethod(targetType);
            set = (IQueryable)whereMethod.Invoke(null, new object[] { set, lambda })!;
        }

        return set;
    }

    private bool TargetExistsInScope(DbContext db, Type targetType, ResourceContract? targetContract, object fkVal)
    {
        var keyProp = ResolveTargetKeyProperty(targetType, targetContract);
        if (keyProp is null) return true; // cannot introspect: defer to DB FK constraints

        var set = ScopedTargetQuery(db, targetType, targetContract);

        var param = System.Linq.Expressions.Expression.Parameter(targetType, "t");
        var member = System.Linq.Expressions.Expression.Property(param, keyProp.Name);
        object typedVal;
        try { typedVal = CoerceId(fkVal, member.Type); }
        catch (Exception) { return false; }
        var eq = System.Linq.Expressions.Expression.Equal(
            member, System.Linq.Expressions.Expression.Constant(typedVal, member.Type));
        var lambda = System.Linq.Expressions.Expression.Lambda(eq, param);

        var anyMethod = typeof(Queryable).GetMethods()
            .First(m => m.Name == nameof(Queryable.Any) && m.GetParameters().Length == 2)
            .MakeGenericMethod(targetType);

        return (bool)anyMethod.Invoke(null, new object[] { set, lambda })!;
    }

    private void ReplaceCollectionByIds(
        object entity,
        System.Reflection.PropertyInfo navProp,
        Type targetType,
        ResourceContract? targetContract,
        string writeFieldName,
        IReadOnlyList<object> ids,
        DbContext db)
    {
        // Load targets: SELECT * WHERE Id IN (...) — scoped by tenant / row-level security /
        // soft-delete so callers can only attach rows they are allowed to see.
        var set = ScopedTargetQuery(db, targetType, targetContract);
        var idProp = ResolveTargetKeyProperty(targetType, targetContract);
        if (idProp == null) return;

        var param = System.Linq.Expressions.Expression.Parameter(targetType, "t");
        var member = System.Linq.Expressions.Expression.Property(param, idProp.Name);

        // ids.Contains(t.Id)
        var containsMethod = typeof(Enumerable).GetMethods()
            .First(m => m.Name == nameof(Enumerable.Contains) && m.GetParameters().Length == 2)
            .MakeGenericMethod(member.Type);

        var idsArray = Array.CreateInstance(member.Type, ids.Count);
        for (int i = 0; i < ids.Count; i++) idsArray.SetValue(CoerceId(ids[i], member.Type), i);

        var idsConst = System.Linq.Expressions.Expression.Constant(idsArray);
        var containsCall = System.Linq.Expressions.Expression.Call(null, containsMethod, idsConst, member);
        var lambda = System.Linq.Expressions.Expression.Lambda(containsCall, param);

        var whereMethod = typeof(Queryable).GetMethods()
            .First(m => m.Name == nameof(Queryable.Where) && m.GetParameters().Length == 2)
            .MakeGenericMethod(targetType);

        var filtered = (IQueryable)whereMethod.Invoke(null, new object[] { set, lambda })!;
        var toListMethod = typeof(Enumerable).GetMethods()
            .First(m => m.Name == nameof(Enumerable.ToList) && m.GetParameters().Length == 1)
            .MakeGenericMethod(targetType);

        var loadedList = (IList)toListMethod.Invoke(null, new object[] { filtered })!;

        // Every requested id must resolve to a visible row; silently dropping unknown or
        // inaccessible ids would mask client errors and existence probes alike.
        var distinctRequested = ids.Select(i => CoerceId(i, member.Type)).Distinct().Count();
        if (loadedList.Count != distinctRequested)
        {
            throw new CrudRequestValidationException(new Dictionary<string, string[]>
            {
                [writeFieldName] = new[] { "One or more related entities do not exist or are not accessible." }
            });
        }

        // Replace collection
        var current = navProp.GetValue(entity);
        if (current is IList list)
        {
            list.Clear();
            foreach (var item in loadedList) list.Add(item);
            return;
        }

        // If property type isn't IList, just set a new List<TTarget> if possible
        var concreteListType = typeof(List<>).MakeGenericType(targetType);
        var newList = (IList)Activator.CreateInstance(concreteListType)!;
        foreach (var item in loadedList) newList.Add(item);

        if (navProp.CanWrite) navProp.SetValue(entity, newList);
    }

    // Resolves the target's key property, preferring the target contract's declared key over
    // the Id / {TypeName}Id naming convention.
    private static System.Reflection.PropertyInfo? ResolveTargetKeyProperty(Type targetType, ResourceContract? targetContract)
    {
        if (targetContract is not null)
        {
            var byContract = targetType.GetProperty(targetContract.Key.Name);
            if (byContract is not null) return byContract;
        }
        return targetType.GetProperty("Id") ?? targetType.GetProperty(targetType.Name + "Id");
    }

    // Convert.ChangeType alone throws for Guid/enum ids.
    private static object CoerceId(object id, Type targetType)
    {
        var t = Nullable.GetUnderlyingType(targetType) ?? targetType;
        if (t.IsInstanceOfType(id)) return id;
        if (t == typeof(string)) return id.ToString()!;
        if (t == typeof(Guid)) return id is Guid g ? g : Guid.Parse(id.ToString()!);
        if (t.IsEnum) return id is string es ? Enum.Parse(t, es, ignoreCase: true) : Enum.ToObject(t, id);
        return Convert.ChangeType(id, t, System.Globalization.CultureInfo.InvariantCulture);
    }

    private static IQueryable GetDbSet(DbContext db, Type entityType)
    {
        var m = typeof(DbContext).GetMethod(nameof(DbContext.Set), Type.EmptyTypes)!;
        var gm = m.MakeGenericMethod(entityType);
        return (IQueryable)gm.Invoke(db, Array.Empty<object>())!;
    }

    private object? DeserializeNode(JsonNode? node, Type targetType)
    {
        if (node is null) return null;

        // JsonNode -> JSON -> target type
        // (fast enough; can optimize later)
        var json = node.ToJsonString();
        return JsonSerializer.Deserialize(json, targetType, _json);
    }

    private static (Type Target, bool IsCollection) GetNavigationTarget(Type t)
    {
        if (t == typeof(string)) return (t, false);
        if (t.IsGenericType && typeof(IEnumerable).IsAssignableFrom(t))
        {
            var arg = t.GetGenericArguments().FirstOrDefault() ?? typeof(object);
            return (arg, true);
        }
        return (t, false);
    }

    private IReadOnlyList<object> DeserializeIds(JsonNode node, Type targetType, ResourceContract? targetContract)
    {
        var idProp = ResolveTargetKeyProperty(targetType, targetContract);
        var idType = idProp?.PropertyType ?? typeof(int);

        if (node is JsonArray arr)
        {
            var list = new List<object>(arr.Count);
            foreach (var item in arr)
            {
                if (item is null) continue;
                var obj = DeserializeNode(item, idType);
                if (obj != null) list.Add(obj);
            }
            return list;
        }

        // fallback: single id treated as list of one
        var one = DeserializeNode(node, idType);
        return one == null ? Array.Empty<object>() : new[] { one };
    }
}
