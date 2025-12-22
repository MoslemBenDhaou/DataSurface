namespace DataSurface.EFCore.Exceptions;

/// <summary>
/// Exception thrown when a CRUD request payload fails validation.
/// </summary>
public sealed class CrudRequestValidationException : Exception
{
    /// <summary>
    /// Gets the validation errors keyed by field.
    /// </summary>
    public IReadOnlyDictionary<string, string[]> Errors { get; }

    /// <summary>
    /// Creates a new instance.
    /// </summary>
    /// <param name="errors">Validation errors keyed by field.</param>
    public CrudRequestValidationException(IDictionary<string, string[]> errors)
        : base("CRUD request validation failed.")
    {
        Errors = new Dictionary<string, string[]>(errors, StringComparer.OrdinalIgnoreCase);
    }
}

/// <summary>
/// Exception thrown when a requested record cannot be found.
/// </summary>
public sealed class CrudNotFoundException : Exception
{
    /// <summary>
    /// Creates a new instance.
    /// </summary>
    /// <param name="resourceKey">The resource key being accessed.</param>
    /// <param name="id">The requested record identifier.</param>
    public CrudNotFoundException(string resourceKey, object id)
        : base($"'{resourceKey}' record not found for id '{id}'.") { }
}

/// <summary>
/// Exception thrown when a concurrency conflict is detected during a CRUD operation.
/// </summary>
public sealed class CrudConcurrencyException : Exception
{
    /// <summary>
    /// Gets the resource key.
    /// </summary>
    public string ResourceKey { get; }

    /// <summary>
    /// Gets the entity identifier.
    /// </summary>
    public object Id { get; }

    /// <summary>
    /// Creates a new instance.
    /// </summary>
    /// <param name="resourceKey">The resource key being accessed.</param>
    /// <param name="id">The record identifier.</param>
    /// <param name="message">Optional message describing the conflict.</param>
    public CrudConcurrencyException(string resourceKey, object id, string? message = null)
        : base(message ?? $"Concurrency conflict for '{resourceKey}' record with id '{id}'.")
    {
        ResourceKey = resourceKey;
        Id = id;
    }
}
