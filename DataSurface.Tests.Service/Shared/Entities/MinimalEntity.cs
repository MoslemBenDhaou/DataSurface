using DataSurface.Core.Annotations;
using DataSurface.Core.Enums;

namespace DataSurface.Tests.Service.Shared.Entities;

/// <summary>
/// Minimal entity with only a key and one field — tests contract builder baseline behavior.
/// </summary>
[CrudResource("minimal-items")]
public class MinimalEntity
{
    [CrudKey]
    [CrudField(CrudDto.Read | CrudDto.Filter | CrudDto.Sort)]
    public int Id { get; set; }

    [CrudField(CrudDto.Read | CrudDto.Create | CrudDto.Update)]
    public string Value { get; set; } = "";
}

/// <summary>
/// Entity with no CrudField attributes — tests opt-in behavior when ExposeFieldsOnlyWhenAnnotated = true.
/// </summary>
[CrudResource("bare-items")]
public class BareEntity
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
}

/// <summary>
/// Entity with all operations disabled except List and Get.
/// </summary>
[CrudResource("readonly-items", EnableCreate = false, EnableUpdate = false, EnableDelete = false)]
public class ReadOnlyEntity
{
    [CrudKey]
    [CrudField(CrudDto.Read | CrudDto.Filter | CrudDto.Sort)]
    public int Id { get; set; }

    [CrudField(CrudDto.Read | CrudDto.Filter | CrudDto.Sort, Searchable = true)]
    public string Title { get; set; } = "";
}

/// <summary>
/// Entity with a custom resource key.
/// </summary>
[CrudResource("custom-items", ResourceKey = "CustomItem")]
public class CustomKeyEntity
{
    [CrudKey(ApiName = "itemId")]
    [CrudField(CrudDto.Read | CrudDto.Filter)]
    public Guid Id { get; set; }

    [CrudField(CrudDto.Read | CrudDto.Create | CrudDto.Update | CrudDto.Filter)]
    public string Label { get; set; } = "";
}

/// <summary>
/// Entity with a string key.
/// </summary>
[CrudResource("slugged-items", KeyProperty = "Slug")]
public class StringKeyEntity
{
    [CrudKey]
    [CrudField(CrudDto.Read | CrudDto.Filter)]
    public string Slug { get; set; } = "";

    [CrudField(CrudDto.Read | CrudDto.Create | CrudDto.Update)]
    public string Content { get; set; } = "";
}

/// <summary>
/// Entity with duplicate route — tests contract builder validation.
/// </summary>
[CrudResource("products")]
public class DuplicateRouteEntity
{
    [CrudKey]
    [CrudField(CrudDto.Read)]
    public int Id { get; set; }
}

/// <summary>
/// Entity with no key attribute — tests contract builder key inference.
/// </summary>
[CrudResource("no-key-items")]
public class NoExplicitKeyEntity
{
    [CrudField(CrudDto.Read | CrudDto.Filter)]
    public int Id { get; set; }

    [CrudField(CrudDto.Read | CrudDto.Create)]
    public string Name { get; set; } = "";
}

/// <summary>
/// Entity with a relation.
/// </summary>
[CrudResource("categories")]
public class CategoryEntity
{
    [CrudKey]
    [CrudField(CrudDto.Read | CrudDto.Filter)]
    public int Id { get; set; }

    [CrudField(CrudDto.Read | CrudDto.Create | CrudDto.Update, RequiredOnCreate = true)]
    public string Name { get; set; } = "";

    [CrudRelation(ReadExpandAllowed = true)]
    public ICollection<ProductEntity> Products { get; set; } = new List<ProductEntity>();
}
