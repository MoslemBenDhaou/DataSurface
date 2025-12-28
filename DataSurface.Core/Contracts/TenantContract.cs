namespace DataSurface.Core.Contracts;

/// <summary>
/// Tenant isolation configuration for a resource.
/// </summary>
/// <param name="FieldName">The CLR property name that holds the tenant ID.</param>
/// <param name="FieldApiName">The API name of the tenant field.</param>
/// <param name="ClaimType">The claim type used to resolve the tenant ID from the current user.</param>
/// <param name="Required">Whether to throw an exception if the tenant claim is missing.</param>
public sealed record TenantContract(
    string FieldName,
    string FieldApiName,
    string ClaimType,
    bool Required
);
