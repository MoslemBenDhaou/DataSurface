using DataSurface.Core.Annotations;
using DataSurface.Core.Enums;

namespace DataSurface.Tests.Service.Shared.Entities;

/// <summary>
/// Test entity with concurrency and tenant isolation.
/// </summary>
[CrudResource("orders", MaxPageSize = 100)]
[CrudAuthorize("OrderAdmin")]
public class OrderEntity
{
    [CrudKey]
    [CrudField(CrudDto.Read | CrudDto.Filter | CrudDto.Sort)]
    public Guid Id { get; set; }

    [CrudField(CrudDto.Read | CrudDto.Create | CrudDto.Filter | CrudDto.Sort,
        RequiredOnCreate = true)]
    public string CustomerName { get; set; } = "";

    [CrudField(CrudDto.Read | CrudDto.Create | CrudDto.Update | CrudDto.Filter | CrudDto.Sort,
        RequiredOnCreate = true)]
    public decimal TotalAmount { get; set; }

    [CrudField(CrudDto.Read | CrudDto.Create | CrudDto.Update | CrudDto.Filter,
        AllowedValues = "pending|confirmed|shipped|delivered|cancelled", DefaultValue = "pending")]
    public string Status { get; set; } = "pending";

    [CrudField(CrudDto.Read | CrudDto.Filter | CrudDto.Sort)]
    public DateTime OrderDate { get; set; } = DateTime.UtcNow;

    [CrudField(CrudDto.Read | CrudDto.Update)]
    public string? ShippingAddress { get; set; }

    [CrudConcurrency(Mode = ConcurrencyMode.RowVersion)]
    [CrudField(CrudDto.Read)]
    public byte[]? RowVersion { get; set; }

    [CrudTenant(ClaimType = "tenant_id", Required = true)]
    public string? TenantId { get; set; }
}
