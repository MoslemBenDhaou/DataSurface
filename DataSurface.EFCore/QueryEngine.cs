using System.Linq.Expressions;
using DataSurface.Core.Contracts;
using DataSurface.EFCore.Contracts;
using DataSurface.EFCore.Exceptions;

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

        if (!string.IsNullOrWhiteSpace(spec.Sort))
            query = ApplySort(query, contract, spec.Sort!);

        return query.Skip((page - 1) * pageSize).Take(pageSize);
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

    private static IQueryable<TEntity> ApplyFilters<TEntity>(
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
            if (!allowed.Contains(apiField)) continue;

            var field = contract.Fields.FirstOrDefault(f => f.ApiName.Equals(apiField, StringComparison.OrdinalIgnoreCase));
            if (field == null) continue;

            // format: "op:value" or "value" => default eq
            var (op, value) = ParseOp(raw);
            var expr = BuildPredicate<TEntity>(param, field.Name, op, value);

            combined = combined == null ? expr : Expression.AndAlso(combined, expr);
        }

        if (combined == null) return query;

        var lambda = Expression.Lambda<Func<TEntity, bool>>(combined, param);
        return query.Where(lambda);
    }

    private static (string op, string value) ParseOp(string raw)
    {
        var idx = raw.IndexOf(':');
        if (idx <= 0) return ("eq", raw);
        return (raw[..idx].Trim(), raw[(idx + 1)..].Trim());
    }

    private static Expression BuildPredicate<TEntity>(
        ParameterExpression param,
        string clrPropName,
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
            var typedValues = values.Select(v => nonNull == typeof(string) ? v : ConvertTo(nonNull, v)).ToList();
            
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

        object? typed = rawValue;
        if (nonNull != typeof(string))
            typed = ConvertTo(nonNull, rawValue);

        var valueConstant = Expression.Constant(typed, nonNull);

        Expression leftExpr = prop;
        if (isNullable && propType.IsValueType)
            leftExpr = Expression.Convert(prop, nonNull);

        return op.ToLowerInvariant() switch
        {
            "eq"  => Expression.Equal(leftExpr, valueConstant),
            "neq" => Expression.NotEqual(leftExpr, valueConstant),
            "gt"  => Expression.GreaterThan(leftExpr, valueConstant),
            "gte" => Expression.GreaterThanOrEqual(leftExpr, valueConstant),
            "lt"  => Expression.LessThan(leftExpr, valueConstant),
            "lte" => Expression.LessThanOrEqual(leftExpr, valueConstant),

            "contains" when nonNull == typeof(string)
                => Expression.Call(leftExpr, nameof(string.Contains), Type.EmptyTypes, valueConstant),

            "starts" when nonNull == typeof(string)
                => Expression.Call(leftExpr, nameof(string.StartsWith), Type.EmptyTypes, valueConstant),

            "ends" when nonNull == typeof(string)
                => Expression.Call(leftExpr, nameof(string.EndsWith), Type.EmptyTypes, valueConstant),

            _ => Expression.Equal(leftExpr, valueConstant)
        };
    }

    private static object ConvertTo(Type t, string raw)
    {
        try
        {
            if (t == typeof(int)) return int.Parse(raw);
            if (t == typeof(long)) return long.Parse(raw);
            if (t == typeof(decimal)) return decimal.Parse(raw, System.Globalization.CultureInfo.InvariantCulture);
            if (t == typeof(bool)) return bool.Parse(raw);
            if (t == typeof(Guid)) return Guid.Parse(raw);
            if (t == typeof(DateTime)) return DateTime.Parse(raw, System.Globalization.CultureInfo.InvariantCulture);

            if (t.IsEnum) return Enum.Parse(t, raw, ignoreCase: true);

            return raw;
        }
        catch (Exception ex) when (ex is FormatException or OverflowException or ArgumentException)
        {
            throw new CrudRequestValidationException(new Dictionary<string, string[]>
            {
                ["filter"] = new[] { $"Invalid filter value '{raw}' for type {t.Name}." }
            });
        }
    }

    private static IQueryable<TEntity> ApplySort<TEntity>(
        IQueryable<TEntity> query,
        ResourceContract contract,
        string sort)
        where TEntity : class
    {
        var allowed = new HashSet<string>(contract.Query.SortableFields, StringComparer.OrdinalIgnoreCase);

        // sort="title,-id"
        var parts = sort.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        bool first = true;

        foreach (var part in parts)
        {
            var desc = part.StartsWith("-");
            var apiName = desc ? part[1..] : part;

            if (!allowed.Contains(apiName)) continue;

            var field = contract.Fields.FirstOrDefault(f => f.ApiName.Equals(apiName, StringComparison.OrdinalIgnoreCase));
            if (field == null) continue;

            query = ApplyOrder(query, field.Name, desc, first);
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
