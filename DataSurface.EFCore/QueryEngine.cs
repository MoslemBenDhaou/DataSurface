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

        if (!string.IsNullOrWhiteSpace(spec.Sort))
            query = ApplySort(query, contract, spec.Sort!);

        return query.Skip((page - 1) * pageSize).Take(pageSize);
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

        object? typed = rawValue;
        if (nonNull != typeof(string))
            typed = ConvertTo(nonNull, rawValue);

        var constant = Expression.Constant(typed, nonNull);

        Expression left = prop;
        if (Nullable.GetUnderlyingType(propType) != null)
            left = Expression.Convert(prop, nonNull);

        return op.ToLowerInvariant() switch
        {
            "eq"  => Expression.Equal(left, constant),
            "neq" => Expression.NotEqual(left, constant),
            "gt"  => Expression.GreaterThan(left, constant),
            "gte" => Expression.GreaterThanOrEqual(left, constant),
            "lt"  => Expression.LessThan(left, constant),
            "lte" => Expression.LessThanOrEqual(left, constant),

            "contains" when nonNull == typeof(string)
                => Expression.Call(left, nameof(string.Contains), Type.EmptyTypes, constant),

            "starts" when nonNull == typeof(string)
                => Expression.Call(left, nameof(string.StartsWith), Type.EmptyTypes, constant),

            "ends" when nonNull == typeof(string)
                => Expression.Call(left, nameof(string.EndsWith), Type.EmptyTypes, constant),

            _ => Expression.Equal(left, constant)
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
