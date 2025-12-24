namespace DataSurface.Core.Annotations;

/// <summary>
/// Marks a property as the tenant identifier for automatic multi-tenancy isolation.
/// When applied, all CRUD operations will automatically filter by the current tenant.
/// </summary>
/// <remarks>
/// The tenant value is resolved from the current user's claims or a custom tenant resolver.
/// </remarks>
[AttributeUsage(AttributeTargets.Property, AllowMultiple = false)]
public sealed class CrudTenantAttribute : Attribute
{
    /// <summary>
    /// Gets or sets the claim type used to resolve the tenant ID from the current user.
    /// Defaults to "tenant_id".
    /// </summary>
    public string ClaimType { get; set; } = "tenant_id";

    /// <summary>
    /// Gets or sets whether to throw an exception if the tenant claim is missing.
    /// If false, operations will proceed without tenant filtering.
    /// </summary>
    public bool Required { get; set; } = true;
}
