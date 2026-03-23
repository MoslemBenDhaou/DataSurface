using DataSurface.Core.Annotations;
using DataSurface.Core.Enums;

namespace DataSurface.Tests.Service.Shared.Entities;

/// <summary>
/// A richly-annotated test entity covering many contract builder scenarios:
/// key, fields with various DTO memberships, validation rules, search, defaults, computed, immutable, hidden.
/// </summary>
[CrudResource("products", MaxPageSize = 50, EnableDelete = false)]
public class ProductEntity
{
    [CrudKey]
    [CrudField(CrudDto.Read | CrudDto.Filter | CrudDto.Sort)]
    public int Id { get; set; }

    [CrudField(CrudDto.Read | CrudDto.Create | CrudDto.Update | CrudDto.Filter | CrudDto.Sort,
        RequiredOnCreate = true, Searchable = true)]
    public string Name { get; set; } = "";

    [CrudField(CrudDto.Read | CrudDto.Create | CrudDto.Update, Searchable = true)]
    public string? Description { get; set; }

    [CrudField(CrudDto.Read | CrudDto.Create | CrudDto.Update | CrudDto.Filter | CrudDto.Sort,
        RequiredOnCreate = true)]
    public decimal Price { get; set; }

    [CrudField(CrudDto.Read | CrudDto.Create | CrudDto.Filter,
        AllowedValues = "active|discontinued|draft", DefaultValue = "draft")]
    public string Status { get; set; } = "draft";

    [CrudField(CrudDto.Read | CrudDto.Create, Immutable = true)]
    public string? Sku { get; set; }

    [CrudField(CrudDto.Read | CrudDto.Filter | CrudDto.Sort)]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [CrudField(CrudDto.Read, ComputedExpression = "Name + ' (' + Status + ')'")]
    public string? DisplayName { get; set; }

    [CrudHidden]
    public string? InternalNotes { get; set; }

    [CrudIgnore]
    public string? TransientData { get; set; }
}
