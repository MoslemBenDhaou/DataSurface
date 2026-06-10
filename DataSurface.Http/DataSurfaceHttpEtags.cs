// This assumes your Read response includes a concurrency field (e.g. rowVersion) when enabled.

using System.Text.Json.Nodes;
using DataSurface.Core.Contracts;
using DataSurface.Core.Enums;
using Microsoft.AspNetCore.Http;

namespace DataSurface.Http;

/// <summary>
/// Helpers for emitting and consuming HTTP ETags for DataSurface resources.
/// </summary>
public static class DataSurfaceHttpEtags
{
    // ETag = W/"base64(rowVersion)"
    /// <summary>
    /// Attempts to set the <c>ETag</c> response header from a row-version style concurrency token.
    /// </summary>
    /// <param name="res">The outgoing HTTP response.</param>
    /// <param name="c">The resource contract used to locate concurrency configuration.</param>
    /// <param name="body">The response body containing the concurrency field.</param>
    /// <param name="enabled">Whether ETag support is enabled.</param>
    /// <returns>The ETag value if set; otherwise <c>null</c>.</returns>
    public static string? TrySetEtag(HttpResponse res, ResourceContract c, JsonObject body, bool enabled)
    {
        if (!enabled) return null;

        var cc = c.Operations.TryGetValue(CrudOperation.Update, out var oc) ? oc.Concurrency : null;
        if (cc is null || cc.Mode != ConcurrencyMode.RowVersion) return null;

        if (!body.TryGetPropertyValue(cc.FieldApiName, out var node) || node is null) return null;

        var token = node.ToJsonString().Trim('"');
        if (string.IsNullOrWhiteSpace(token)) return null;

        var etag = $"W/\"{token}\"";
        res.Headers.ETag = etag;
        return etag;
    }

    // If-Match: W/"token" or "token" -> token
    /// <summary>
    /// Extracts the concurrency token from the <c>If-Match</c> request header, if present.
    /// </summary>
    /// <param name="req">The incoming HTTP request.</param>
    /// <returns>The token value if present; otherwise <c>null</c>.</returns>
    /// <remarks>
    /// <c>If-Match: *</c> means "proceed if the resource exists" (RFC 9110) and yields no token —
    /// the by-id lookup already guarantees existence, so no concurrency check applies.
    /// </remarks>
    public static string? GetIfMatchToken(HttpRequest req)
    {
        if (!req.Headers.TryGetValue("If-Match", out var v)) return null;
        var raw = v.ToString();
        if (string.IsNullOrWhiteSpace(raw)) return null;

        raw = raw.Trim();

        // RFC 9110: '*' matches any current representation; it is not a literal token.
        if (raw == "*") return null;

        return StripEtagDecorations(raw);
    }

    /// <summary>
    /// Compares an <c>If-None-Match</c> header (which may carry a comma-separated list, weak
    /// prefixes, or <c>*</c>) against the response ETag using weak comparison.
    /// </summary>
    /// <param name="ifNoneMatch">The raw If-None-Match header values.</param>
    /// <param name="etag">The ETag of the current representation.</param>
    public static bool IfNoneMatchMatches(Microsoft.Extensions.Primitives.StringValues ifNoneMatch, string etag)
    {
        if (ifNoneMatch.Count == 0) return false;

        var current = StripEtagDecorations(etag.Trim());

        foreach (var headerValue in ifNoneMatch)
        {
            if (string.IsNullOrWhiteSpace(headerValue)) continue;

            foreach (var candidate in headerValue.Split(','))
            {
                var trimmed = candidate.Trim();
                if (trimmed.Length == 0) continue;
                if (trimmed == "*") return true;
                if (StripEtagDecorations(trimmed) == current) return true;
            }
        }

        return false;
    }

    // Removes the weak prefix and surrounding quotes (weak comparison per RFC 9110 §8.8.3.2).
    private static string StripEtagDecorations(string raw)
    {
        if (raw.StartsWith("W/", StringComparison.OrdinalIgnoreCase))
            raw = raw[2..].Trim();

        if (raw.Length >= 2 && raw.StartsWith('"') && raw.EndsWith('"'))
            return raw[1..^1];

        return raw;
    }

    // If-Match -> add into patch as concurrency field if missing
    /// <summary>
    /// If ETags are enabled, copies the <c>If-Match</c> token into the patch object using the configured
    /// concurrency field name when that field is not already present.
    /// </summary>
    /// <param name="c">The resource contract used to locate concurrency configuration.</param>
    /// <param name="req">The incoming HTTP request.</param>
    /// <param name="patch">The patch payload to augment.</param>
    /// <param name="enabled">Whether ETag support is enabled.</param>
    public static void ApplyIfMatchToPatch(ResourceContract c, HttpRequest req, JsonObject patch, bool enabled)
    {
        if (!enabled) return;

        var cc = c.Operations.TryGetValue(CrudOperation.Update, out var oc) ? oc.Concurrency : null;
        if (cc is null || cc.Mode != ConcurrencyMode.RowVersion) return;

        // Case-insensitive: the rest of the pipeline matches body keys case-insensitively.
        if (patch.Any(kv => string.Equals(kv.Key, cc.FieldApiName, StringComparison.OrdinalIgnoreCase))) return;

        var token = GetIfMatchToken(req);
        if (!string.IsNullOrWhiteSpace(token))
            patch[cc.FieldApiName] = token;
    }
}
