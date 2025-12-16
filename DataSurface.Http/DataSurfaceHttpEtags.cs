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
    public static void TrySetEtag(HttpResponse res, ResourceContract c, JsonObject body, bool enabled)
    {
        if (!enabled) return;

        var cc = c.Operations.TryGetValue(CrudOperation.Update, out var oc) ? oc.Concurrency : null;
        if (cc is null || cc.Mode != ConcurrencyMode.RowVersion) return;

        if (!body.TryGetPropertyValue(cc.FieldApiName, out var node) || node is null) return;

        var token = node.ToJsonString().Trim('"');
        if (string.IsNullOrWhiteSpace(token)) return;

        res.Headers.ETag = $"W/\"{token}\"";
    }

    // If-Match: W/"token" or "token" -> token
    /// <summary>
    /// Extracts the concurrency token from the <c>If-Match</c> request header, if present.
    /// </summary>
    /// <param name="req">The incoming HTTP request.</param>
    /// <returns>The token value if present; otherwise <c>null</c>.</returns>
    public static string? GetIfMatchToken(HttpRequest req)
    {
        if (!req.Headers.TryGetValue("If-Match", out var v)) return null;
        var raw = v.ToString();
        if (string.IsNullOrWhiteSpace(raw)) return null;

        raw = raw.Trim();

        // W/"...":
        if (raw.StartsWith("W/\"", StringComparison.OrdinalIgnoreCase) && raw.EndsWith("\""))
            return raw.Substring(3, raw.Length - 4);

        // "..."
        if (raw.StartsWith("\"") && raw.EndsWith("\""))
            return raw.Substring(1, raw.Length - 2);

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

        if (patch.ContainsKey(cc.FieldApiName)) return;

        var token = GetIfMatchToken(req);
        if (!string.IsNullOrWhiteSpace(token))
            patch[cc.FieldApiName] = token;
    }
}
