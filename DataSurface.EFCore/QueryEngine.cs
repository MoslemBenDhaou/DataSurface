using System.Linq.Expressions;
using DataSurface.Core.Contracts;
using DataSurface.EFCore.Contracts;
using DataSurface.EFCore.Exceptions;
using DataSurface.EFCore.Options;

namespace DataSurface.EFCore;

/// <summary>
/// Applies DataSurface query semantics (filtering, sorting, paging) to an EF Core <see cref="IQueryable{T}"/>.
/// </summary>
/// <remarks>
/// Filtering and sorting are constrained by the allowlists in <see cref="ResourceContract.Query"/>
/// (<see cref="QueryContract.FilterableFields"/> and <see cref="QueryContract.SortableFields"/>).
/// Fields outside the allowlists are ignored.
/// </remarks>
public sealed class EfCrudQueryEngine
{
    private readonly bool _strict;

    private static readonly HashSet<string> KnownOps = new(StringComparer.OrdinalIgnoreCase)
    {
        "eq", "neq", "gt", "gte", "lt", "lte", "contains", "starts", "ends", "in", "isnull"
    };

    /// <summary>
    /// Creates a new query engine.
    /// </summary>
    /// <param name="options">EF Core options. When <see cref="DataSurfaceEfCoreOptions.StrictQuery"/> is
    /// set, disallowed filter/sort fields are rejected instead of ignored.</param>
    public EfCrudQueryEngine(DataSurfaceEfCoreOptions? options = null)
        => _strict = options?.StrictQuery ?? false;

    /// <summary>
    /// Applies <paramref name="spec"/> to <paramref name="query"/> using the allowlists and limits defined in
    /// <paramref name="contract"/>.
    /// </summary>
    /// <typeparam name="TEntity">Entity CLR type.</typeparam>
    /// <param name="query">Base query.</param>
    /// <param name="contract">Resource contract that defines filter/sort allowlists and paging limits.</param>
    /// <param name="spec">Requested paging, sorting, and filtering.</param>
    /// <returns>An updated query with filtering, sorting and paging applied.</returns>
    /// <remarks>
    /// - Page is clamped to a minimum of 1.
    /// - PageSize is clamped to <c>1..contract.Query.MaxPageSize</c>.
    /// - Filter syntax supports <c>"op:value"</c> (for example <c>"gte:10"</c>) or plain <c>"value"</c>
    ///   (defaults to equality).
    /// - Sort supports comma-separated fields with optional <c>-</c> prefix for descending order (for example
    ///   <c>"title,-id"</c>).
    /// - When neither the request nor the contract specifies a sort, results are ordered by the key;
    ///   the key is always appended as a final tie-breaker. Skip/Take without a deterministic ORDER BY
    ///   produces nondeterministic pages (rows repeated or skipped).
    /// </remarks>
    public IQueryable<TEntity> Apply<TEntity>(
        IQueryable<TEntity> query,
        ResourceContract contract,
        QuerySpec spec)
        where TEntity : class
    {
        var page = Math.Max(1, spec.Page);
        var pageSize = Math.Clamp(spec.PageSize, 1, contract.Query.MaxPageSize);

        if (spec.Filters != null && spec.Filters.Count > 0)
            query = ApplyFilters(query, contract, spec.Filters);

        if (!string.IsNullOrWhiteSpace(spec.Search))
            query = ApplySearch(query, contract, spec.Search!);

        query = ApplyDeterministicSort(query, contract, spec);

        // (page-1)*pageSize can overflow int for hostile page values; compute in long and clamp.
        var skip = (long)(page - 1) * pageSize;
        if (skip > int.MaxValue) skip = int.MaxValue;

        return query.Skip((int)skip).Take(pageSize);
    }

    /// <summary>
    /// Applies filtering, searching and sorting from <paramref name="spec"/> without pagination.
    /// Use this to obtain a filtered query suitable for counting before applying Skip/Take.
    /// </summary>
    public IQueryable<TEntity> ApplyFiltersAndSort<TEntity>(
        IQueryable<TEntity> query,
        ResourceContract contract,
        QuerySpec spec)
        where TEntity : class
    {
        if (spec.Filters != null && spec.Filters.Count > 0)
            query = ApplyFilters(query, contract, spec.Filters);

        if (!string.IsNullOrWhiteSpace(spec.Search))
            query = ApplySearch(query, contract, spec.Search!);

        var sort = EffectiveSort(contract, spec);
        if (!string.IsNullOrWhiteSpace(sort))
            query = ApplySort(query, contract, sort!, out _);

        return query;
    }

    // The requested sort wins; the contract's DefaultSort fills in when the client sent none.
    private static string? EffectiveSort(ResourceContract contract, QuerySpec spec)
        => !string.IsNullOrWhiteSpace(spec.Sort) ? spec.Sort : contract.Query.DefaultSort;

    // Applies the effective sort and guarantees a deterministic total order by appending the
    // key as a final tie-breaker (or as the only ordering when no sort applies at all).
    private IQueryable<TEntity> ApplyDeterministicSort<TEntity>(
        IQueryable<TEntity> query,
        ResourceContract contract,
        QuerySpec spec)
        where TEntity : class
    {
        var sort = EffectiveSort(contract, spec);
        var sortedByKey = false;
        var applied = false;

        if (!string.IsNullOrWhiteSpace(sort))
        {
            query = ApplySort(query, contract, sort!, out var appliedFields);
            applied = appliedFields.Count > 0;
            sortedByKey = appliedFields.Any(f => f.Equals(contract.Key.Name, StringComparison.OrdinalIgnoreCase));
        }

        if (!sortedByKey && typeof(TEntity).GetProperty(contract.Key.Name) is not null)
            query = ApplyOrder(query, contract.Key.Name, desc: false, first: !applied);

        return query;
    }

    private static IQueryable<TEntity> ApplySearch<TEntity>(
        IQueryable<TEntity> query,
        ResourceContract contract,
        string searchTerm)
        where TEntity : class
    {
        if (contract.Query.SearchableFields.Count == 0) return query;

        var param = Expression.Parameter(typeof(TEntity), "e");
        Expression? combined = null;

        foreach (var apiName in contract.Query.SearchableFields)
        {
            var field = contract.Fields.FirstOrDefault(f => f.ApiName.Equals(apiName, StringComparison.OrdinalIgnoreCase));
            if (field == null) continue;

            var prop = Expression.Property(param, field.Name);
            if (prop.Type != typeof(string)) continue;

            var containsMethod = typeof(string).GetMethod(nameof(string.Contains), new[] { typeof(string) })!;
            var searchConstant = Expression.Constant(searchTerm, typeof(string));
            var nullCheck = Expression.NotEqual(prop, Expression.Constant(null, typeof(string)));
            var containsCall = Expression.Call(prop, containsMethod, searchConstant);
            var safeContains = Expression.AndAlso(nullCheck, containsCall);

            combined = combined == null ? safeContains : Expression.OrElse(combined, safeContains);
        }

        if (combined == null) return query;

        var lambda = Expression.Lambda<Func<TEntity, bool>>(combined, param);
        return query.Where(lambda);
    }

    private IQueryable<TEntity> ApplyFilters<TEntity>(
        IQueryable<TEntity> query,
        ResourceContract contract,
        IReadOnlyDictionary<string, string> filters)
        where TEntity : class
    {
        var allowed = new HashSet<string>(contract.Query.FilterableFields, StringComparer.OrdinalIgnoreCase);
        var param = Expression.Parameter(typeof(TEntity), "e");
        Expression? combined = null;

        foreach (var (apiField, raw) in filters)
        {
            if (!allowed.Contains(apiField))
            {
                if (_strict)
                    throw new CrudRequestValidationException(new Dictionary<string, string[]>
                    {
                        [apiField] = new[] { $"Field '{apiField}' is not filterable." }
                    });
                continue;
            }

            var field = contract.Fields.FirstOrDefault(f => f.ApiName.Equals(apiField, StringComparison.OrdinalIgnoreCase));
            if (field == null) continue;

            // format: "op:value" or "value" => default eq
            var (op, value) = ParseOp(raw);
            var expr = BuildPredicate<TEntity>(param, field.Name, apiField, op, value);

            combined = combined == null ? expr : Expression.AndAlso(combined, expr);
        }

        if (combined == null) return query;

        var lambda = Expression.Lambda<Func<TEntity, bool>>(combined, param);
        return query.Where(lambda);
    }

    // Only a known operator prefix is treated as an operator; anything else is an equality
    // value. Without the whitelist, plain values containing ':' (ISO timestamps, URNs,
    // "ns:key" strings) would be torn apart at the first colon.
    private static (string op, string value) ParseOp(string raw)
    {
        var idx = raw.IndexOf(':');
        if (idx <= 0) return ("eq", raw);

        var prefix = raw[..idx].Trim();
        if (!KnownOps.Contains(prefix)) return ("eq", raw);

        return (prefix, raw[(idx + 1)..].Trim());
    }

    private static Expression BuildPredicate<TEntity>(
        ParameterExpression param,
        string clrPropName,
        string apiField,
        string op,
        string rawValue)
    {
        var prop = Expression.Property(param, clrPropName);
        var propType = prop.Type;
        var nonNull = Nullable.GetUnderlyingType(propType) ?? propType;
        var isNullable = Nullable.GetUnderlyingType(propType) != null || !propType.IsValueType;

        // Handle isnull operator
        if (op.Equals("isnull", StringComparison.OrdinalIgnoreCase))
        {
            if (propType.IsValueType && Nullable.GetUnderlyingType(propType) is null)
                throw FilterError(apiField, "Operator 'isnull' is not supported for non-nullable fields.");

            var isNull = rawValue.Equals("true", StringComparison.OrdinalIgnoreCase);
            var nullConstant = Expression.Constant(null, propType);
            return isNull
                ? Expression.Equal(prop, nullConstant)
                : Expression.NotEqual(prop, nullConstant);
        }

        // Handle 'in' operator for multiple values
        if (op.Equals("in", StringComparison.OrdinalIgnoreCase))
        {
            var values = rawValue.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            var typedValues = values.Select(v => nonNull == typeof(string) ? v : ConvertTo(nonNull, v, apiField)).ToList();

            Expression? inExpr = null;
            foreach (var val in typedValues)
            {
                var constant = Expression.Constant(val, nonNull);
                Expression left = isNullable && propType.IsValueType ? Expression.Convert(prop, nonNull) : prop;
                var eq = Expression.Equal(left, constant);
                inExpr = inExpr == null ? eq : Expression.OrElse(inExpr, eq);
            }
            return inExpr ?? Expression.Constant(false);
        }

        var lowerOp = op.ToLowerInvariant();

        // Operator/type compatibility — turn user errors into 400s, not expression-tree 500s.
        if (lowerOp is "gt" or "gte" or "lt" or "lte" && !IsComparable(nonNull))
            throw FilterError(apiField, $"Operator '{lowerOp}' is not supported for this field type.");
        if (lowerOp is "contains" or "starts" or "ends" && nonNull != typeof(string))
            throw FilterError(apiField, $"Operator '{lowerOp}' is only supported for string fields.");

        object? typed = rawValue;
        if (nonNull != typeof(string))
            typed = ConvertTo(nonNull, rawValue, apiField);

        var valueConstant = Expression.Constant(typed, nonNull);

        Expression leftExpr = prop;
        if (isNullable && propType.IsValueType)
            leftExpr = Expression.Convert(prop, nonNull);

        return lowerOp switch
        {
            "eq"  => Expression.Equal(leftExpr, valueConstant),
            "neq" => Expression.NotEqual(leftExpr, valueConstant),
            "gt"  => Expression.GreaterThan(leftExpr, valueConstant),
            "gte" => Expression.GreaterThanOrEqual(leftExpr, valueConstant),
            "lt"  => Expression.LessThan(leftExpr, valueConstant),
            "lte" => Expression.LessThanOrEqual(leftExpr, valueConstant),

            "contains" => Expression.Call(leftExpr, nameof(string.Contains), Type.EmptyTypes, valueConstant),
            "starts"   => Expression.Call(leftExpr, nameof(string.StartsWith), Type.EmptyTypes, valueConstant),
            "ends"     => Expression.Call(leftExpr, nameof(string.EndsWith), Type.EmptyTypes, valueConstant),

            _ => Expression.Equal(leftExpr, valueConstant)
        };
    }

    private static bool IsComparable(Type t)
        => t == typeof(int) || t == typeof(long) || t == typeof(short) || t == typeof(byte)
        || t == typeof(sbyte) || t == typeof(ushort) || t == typeof(uint) || t == typeof(ulong)
        || t == typeof(decimal) || t == typeof(double) || t == typeof(float)
        || t == typeof(DateTime) || t == typeof(DateTimeOffset)
        || t == typeof(DateOnly) || t == typeof(TimeOnly) || t == typeof(TimeSpan)
        || t == typeof(char);

    private static CrudRequestValidationException FilterError(string apiField, string message)
        => new(new Dictionary<string, string[]> { [apiField] = new[] { message } });

    private static object ConvertTo(Type t, string raw, string apiField)
    {
        var inv = System.Globalization.CultureInfo.InvariantCulture;
        try
        {
            if (t == typeof(int)) return int.Parse(raw, inv);
            if (t == typeof(long)) return long.Parse(raw, inv);
            if (t == typeof(short)) return short.Parse(raw, inv);
            if (t == typeof(byte)) return byte.Parse(raw, inv);
            if (t == typeof(sbyte)) return sbyte.Parse(raw, inv);
            if (t == typeof(ushort)) return ushort.Parse(raw, inv);
            if (t == typeof(uint)) return uint.Parse(raw, inv);
            if (t == typeof(ulong)) return ulong.Parse(raw, inv);
            if (t == typeof(decimal)) return decimal.Parse(raw, inv);
            if (t == typeof(double)) return double.Parse(raw, inv);
            if (t == typeof(float)) return float.Parse(raw, inv);
            if (t == typeof(bool)) return bool.Parse(raw);
            if (t == typeof(Guid)) return Guid.Parse(raw);
            if (t == typeof(DateTime)) return DateTime.Parse(raw, inv, System.Globalization.DateTimeStyles.RoundtripKind);
            if (t == typeof(DateTimeOffset)) return DateTimeOffset.Parse(raw, inv);
            if (t == typeof(DateOnly)) return DateOnly.Parse(raw, inv);
            if (t == typeof(TimeOnly)) return TimeOnly.Parse(raw, inv);
            if (t == typeof(TimeSpan)) return TimeSpan.Parse(raw, inv);
            if (t == typeof(char)) return raw.Length == 1 ? raw[0] : throw new FormatException("Expected a single character.");

            if (t.IsEnum) return Enum.Parse(t, raw, ignoreCase: true);

            // Unknown property type: filtering would otherwise blow up inside expression
            // construction with a 500; reject the request instead.
            throw FilterError(apiField, $"Filtering is not supported for this field type ({t.Name}).");
        }
        catch (Exception ex) when (ex is FormatException or OverflowException or ArgumentException)
        {
            throw FilterError(apiField, $"Invalid filter value '{raw}' for type {t.Name}.");
        }
    }

    private IQueryable<TEntity> ApplySort<TEntity>(
        IQueryable<TEntity> query,
        ResourceContract contract,
        string sort,
        out List<string> appliedClrFields)
        where TEntity : class
    {
        var allowed = new HashSet<string>(contract.Query.SortableFields, StringComparer.OrdinalIgnoreCase);
        appliedClrFields = new List<string>();

        // sort="title,-id"
        var parts = sort.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        bool first = true;

        foreach (var part in parts)
        {
            var desc = part.StartsWith("-");
            var apiName = desc ? part[1..] : part;

            if (!allowed.Contains(apiName))
            {
                if (_strict)
                    throw new CrudRequestValidationException(new Dictionary<string, string[]>
                    {
                        [apiName] = new[] { $"Field '{apiName}' is not sortable." }
                    });
                continue;
            }

            var field = contract.Fields.FirstOrDefault(f => f.ApiName.Equals(apiName, StringComparison.OrdinalIgnoreCase));
            if (field == null) continue;

            query = ApplyOrder(query, field.Name, desc, first);
            appliedClrFields.Add(field.Name);
            first = false;
        }

        return query;
    }

    private static IQueryable<TEntity> ApplyOrder<TEntity>(
        IQueryable<TEntity> query,
        string clrPropName,
        bool desc,
        bool first)
        where TEntity : class
    {
        var param = Expression.Parameter(typeof(TEntity), "e");
        var body = Expression.Property(param, clrPropName);
        var lambda = Expression.Lambda(body, param);

        var method = first
            ? (desc ? "OrderByDescending" : "OrderBy")
            : (desc ? "ThenByDescending" : "ThenBy");

        var m = typeof(Queryable).GetMethods()
            .First(x => x.Name == method && x.GetParameters().Length == 2);

        var gm = m.MakeGenericMethod(typeof(TEntity), body.Type);
        return (IQueryable<TEntity>)gm.Invoke(null, new object[] { query, lambda })!;
    }
}
