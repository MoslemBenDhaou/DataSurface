namespace DataSurface.Core.Contracts;

/// <summary>
/// Per-operation contract describing whether the operation is enabled and its input/output shapes.
/// </summary>
/// <param name="Enabled">Whether the operation is enabled.</param>
/// <param name="InputShape">Allowlist of fields that may appear in the request body (API names).</param>
/// <param name="OutputShape">Allowlist of fields that may appear in the response (API names).</param>
/// <param name="RequiredOnCreate">Fields that are required on create (API names).</param>
/// <param name="ImmutableFields">Fields that are immutable for the resource (API names).</param>
/// <param name="Concurrency">Optional concurrency settings for the operation.</param>
public sealed record OperationContract(
    bool Enabled,
    IReadOnlyList<string> InputShape,   // apiNames
    IReadOnlyList<string> OutputShape,  // apiNames
    IReadOnlyList<string> RequiredOnCreate, // apiNames
    IReadOnlyList<string> ImmutableFields,  // apiNames
    ConcurrencyContract? Concurrency
);