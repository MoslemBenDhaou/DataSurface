using DataSurface.Core.Contracts;
using DataSurface.EFCore.Contracts;
using Microsoft.AspNetCore.Http;

namespace DataSurface.Http;

/// <summary>
/// Parses DataSurface-specific query string parameters into strongly-typed request models.
/// </summary>
public static class DataSurfaceQueryParser
{
    /// <summary>
    /// Parses paging, sorting and filter parameters from the request query string.
    /// </summary>
    /// <param name="req">The incoming HTTP request.</param>
    /// <param name="contract">The resource contract used to validate supported query fields.</param>
    /// <returns>A populated <see cref="QuerySpec"/> instance.</returns>
    public static QuerySpec ParseQuerySpec(HttpRequest req, ResourceContract contract)
    {
        int page = TryInt(req.Query["page"], 1);
        int pageSize = TryInt(req.Query["pageSize"], 20);

        string? sort = req.Query.TryGetValue("sort", out var s) ? s.ToString() : null;

        // filter[field]=op:value
        var filters = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var kv in req.Query)
        {
            var key = kv.Key;
            if (!key.StartsWith("filter[", StringComparison.OrdinalIgnoreCase) || !key.EndsWith("]"))
                continue;

            // extract inside []
            var field = key.Substring("filter[".Length, key.Length - "filter[".Length - 1);
            var value = kv.Value.ToString();

            if (!string.IsNullOrWhiteSpace(field) && !string.IsNullOrWhiteSpace(value))
                filters[field] = value;
        }

        return new QuerySpec(Page: page, PageSize: pageSize, Sort: sort, Filters: filters);
    }

    /// <summary>
    /// Parses an expand request (<c>expand=a,b,c</c>) and filters it to the allowed expand targets for the resource.
    /// </summary>
    /// <param name="req">The incoming HTTP request.</param>
    /// <param name="contract">The resource contract used to validate allowed expansion targets.</param>
    /// <returns>An <see cref="ExpandSpec"/> if any expansions are requested and allowed; otherwise <c>null</c>.</returns>
    public static ExpandSpec? ParseExpand(HttpRequest req, ResourceContract contract)
    {
        if (!req.Query.TryGetValue("expand", out var exp) || string.IsNullOrWhiteSpace(exp))
            return null;

        var asked = exp.ToString()
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToList();

        if (asked.Count == 0) return null;

        var allowed = new HashSet<string>(contract.Read.ExpandAllowed, StringComparer.OrdinalIgnoreCase);
        asked = asked.Where(allowed.Contains).ToList();

        return asked.Count == 0 ? null : new ExpandSpec(asked);
    }

    private static int TryInt(string? s, int fallback)
        => int.TryParse(s, out var v) ? v : fallback;
}
