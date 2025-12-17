using System.Text.Json.Nodes;

namespace DataSurface.EFCore.Contracts;

/// <summary>
/// Specification for a bulk operation request.
/// </summary>
public sealed record BulkOperationSpec
{
    /// <summary>
    /// Gets or sets the items to create.
    /// </summary>
    public IReadOnlyList<JsonObject> Create { get; init; } = [];

    /// <summary>
    /// Gets or sets the items to update (keyed by ID).
    /// </summary>
    public IReadOnlyList<BulkUpdateItem> Update { get; init; } = [];

    /// <summary>
    /// Gets or sets the IDs to delete.
    /// </summary>
    public IReadOnlyList<object> Delete { get; init; } = [];

    /// <summary>
    /// Gets or sets whether to stop on first error or continue processing.
    /// </summary>
    public bool StopOnError { get; init; } = true;

    /// <summary>
    /// Gets or sets whether to wrap all operations in a transaction.
    /// </summary>
    public bool UseTransaction { get; init; } = true;
}

/// <summary>
/// A single update item in a bulk operation.
/// </summary>
public sealed record BulkUpdateItem
{
    /// <summary>
    /// Gets or sets the entity ID.
    /// </summary>
    public required object Id { get; init; }

    /// <summary>
    /// Gets or sets the patch data.
    /// </summary>
    public required JsonObject Patch { get; init; }
}

/// <summary>
/// Result of a bulk operation.
/// </summary>
public sealed record BulkOperationResult
{
    /// <summary>
    /// Gets or sets the created items with their assigned IDs.
    /// </summary>
    public IReadOnlyList<JsonObject> Created { get; init; } = [];

    /// <summary>
    /// Gets or sets the updated items.
    /// </summary>
    public IReadOnlyList<JsonObject> Updated { get; init; } = [];

    /// <summary>
    /// Gets or sets the count of deleted items.
    /// </summary>
    public int DeletedCount { get; init; }

    /// <summary>
    /// Gets or sets any errors that occurred during processing.
    /// </summary>
    public IReadOnlyList<BulkOperationError> Errors { get; init; } = [];

    /// <summary>
    /// Gets whether all operations succeeded.
    /// </summary>
    public bool Success => Errors.Count == 0;
}

/// <summary>
/// An error that occurred during a bulk operation.
/// </summary>
public sealed record BulkOperationError
{
    /// <summary>
    /// Gets or sets the operation type (Create, Update, Delete).
    /// </summary>
    public required string Operation { get; init; }

    /// <summary>
    /// Gets or sets the index of the item that caused the error.
    /// </summary>
    public int Index { get; init; }

    /// <summary>
    /// Gets or sets the entity ID if applicable.
    /// </summary>
    public object? Id { get; init; }

    /// <summary>
    /// Gets or sets the error message.
    /// </summary>
    public required string Message { get; init; }
}
