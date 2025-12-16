using DataSurface.Core.Enums;

namespace DataSurface.Core.Contracts;

/// <summary>
/// Describes the concurrency mechanism for a resource operation.
/// </summary>
/// <param name="Mode">Concurrency mechanism to apply (for example RowVersion).</param>
/// <param name="FieldApiName">API name of the concurrency token field.</param>
/// <param name="RequiredOnUpdate">Whether the token is required during updates.</param>
public sealed record ConcurrencyContract(
    ConcurrencyMode Mode,
    string FieldApiName,
    bool RequiredOnUpdate
);