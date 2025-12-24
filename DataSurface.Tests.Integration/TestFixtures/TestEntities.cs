using DataSurface.Core.Annotations;
using DataSurface.Core.Enums;

namespace DataSurface.Tests.Integration.TestFixtures;

[CrudResource("test-users", MaxPageSize = 100)]
public class CrudTestUser
{
    [CrudField(CrudDto.Read | CrudDto.Filter | CrudDto.Sort)]
    public int Id { get; set; }

    [CrudField(CrudDto.Read | CrudDto.Create | CrudDto.Update | CrudDto.Filter | CrudDto.Sort, Searchable = true, RequiredOnCreate = true)]
    public string Name { get; set; } = "";

    [CrudField(CrudDto.Read | CrudDto.Create | CrudDto.Update | CrudDto.Filter, Searchable = true)]
    public string Email { get; set; } = "";

    [CrudField(CrudDto.Read | CrudDto.Create | CrudDto.Update | CrudDto.Filter, AllowedValues = "active|inactive|pending", DefaultValue = "active")]
    public string? Status { get; set; }

    [CrudField(CrudDto.Read | CrudDto.Filter)]
    public bool IsActive { get; set; } = true;

    [CrudField(CrudDto.Read | CrudDto.Filter | CrudDto.Sort)]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [CrudTenant(ClaimType = "tenant_id")]
    public string? TenantId { get; set; }
}

[CrudResource("test-posts", MaxPageSize = 50)]
public class CrudTestPost
{
    [CrudField(CrudDto.Read | CrudDto.Filter | CrudDto.Sort)]
    public int Id { get; set; }

    [CrudField(CrudDto.Read | CrudDto.Create | CrudDto.Update | CrudDto.Filter | CrudDto.Sort, Searchable = true, RequiredOnCreate = true)]
    public string Title { get; set; } = "";

    [CrudField(CrudDto.Read | CrudDto.Create | CrudDto.Update, Searchable = true)]
    public string? Content { get; set; }

    [CrudField(CrudDto.Read | CrudDto.Create | CrudDto.Update, Searchable = true)]
    public string? Description { get; set; }

    [CrudField(CrudDto.Read | CrudDto.Create | CrudDto.Filter)]
    public int AuthorId { get; set; }

    [CrudField(CrudDto.Read | CrudDto.Create | CrudDto.Update | CrudDto.Filter, AllowedValues = "draft|published|archived", DefaultValue = "draft")]
    public string Status { get; set; } = "draft";

    [CrudField(CrudDto.Read | CrudDto.Create | CrudDto.Update | CrudDto.Filter | CrudDto.Sort)]
    public decimal Price { get; set; }

    [CrudField(CrudDto.Read | CrudDto.Filter | CrudDto.Sort)]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [CrudField(CrudDto.Read, ComputedExpression = "Title + ' - ' + Status")]
    public string? TitleWithStatus { get; set; }
}
