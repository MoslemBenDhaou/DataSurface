namespace DataSurface.Core.Dynamic;

/// <summary>
/// Tracks which API fields were explicitly provided by a client payload.
/// </summary>
/// <remarks>
/// This is useful for PATCH-like semantics where omitted fields should not be modified.
/// </remarks>
public sealed class ProvidedFields
{
    private readonly HashSet<string> _providedApiNames = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Marks an API field name as present in the input payload.
    /// </summary>
    /// <param name="apiName">API field name.</param>
    public void MarkProvided(string apiName) => _providedApiNames.Add(apiName);
    /// <summary>
    /// Returns <see langword="true"/> if the given API field name was present in the input payload.
    /// </summary>
    /// <param name="apiName">API field name.</param>
    public bool IsProvided(string apiName) => _providedApiNames.Contains(apiName);

    /// <summary>
    /// Gets all API field names that were marked as provided.
    /// </summary>
    public IReadOnlyCollection<string> AllProvided => _providedApiNames;
}
