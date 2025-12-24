using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Order;
using DataSurface.Core;
using DataSurface.Core.Annotations;
using DataSurface.Core.Enums;

namespace DataSurface.Benchmarks;

[MemoryDiagnoser]
[Orderer(SummaryOrderPolicy.FastestToSlowest)]
[RankColumn]
public class ContractBuilderBenchmarks
{
    private ContractBuilder _builder = null!;

    [GlobalSetup]
    public void Setup()
    {
        _builder = new ContractBuilder();
    }

    [Benchmark(Baseline = true)]
    public object BuildSimpleEntity()
    {
        return _builder.BuildFromTypes(typeof(SimpleEntity));
    }

    [Benchmark]
    public object BuildEntityWithSearchableFields()
    {
        return _builder.BuildFromTypes(typeof(EntityWithSearchableFields));
    }

    [Benchmark]
    public object BuildEntityWithAllFeatures()
    {
        return _builder.BuildFromTypes(typeof(EntityWithAllFeatures));
    }

    [Benchmark]
    public object BuildMultipleEntities()
    {
        return _builder.BuildFromTypes(
            typeof(SimpleEntity),
            typeof(EntityWithSearchableFields),
            typeof(EntityWithAllFeatures)
        );
    }
}

[CrudResource("simple-entities")]
public class SimpleEntity
{
    [CrudField(CrudDto.Read | CrudDto.Filter | CrudDto.Sort)]
    public int Id { get; set; }

    [CrudField(CrudDto.Read | CrudDto.Create | CrudDto.Update)]
    public string Name { get; set; } = "";
}

[CrudResource("searchable-entities")]
public class EntityWithSearchableFields
{
    [CrudField(CrudDto.Read | CrudDto.Filter | CrudDto.Sort)]
    public int Id { get; set; }

    [CrudField(CrudDto.Read | CrudDto.Create | CrudDto.Update | CrudDto.Filter | CrudDto.Sort, Searchable = true)]
    public string Title { get; set; } = "";

    [CrudField(CrudDto.Read | CrudDto.Create | CrudDto.Update, Searchable = true)]
    public string? Description { get; set; }

    [CrudField(CrudDto.Read | CrudDto.Create | CrudDto.Update, Searchable = true)]
    public string? Content { get; set; }

    [CrudField(CrudDto.Read | CrudDto.Filter | CrudDto.Sort)]
    public DateTime CreatedAt { get; set; }
}

[CrudResource("full-feature-entities", MaxPageSize = 100)]
public class EntityWithAllFeatures
{
    [CrudField(CrudDto.Read | CrudDto.Filter | CrudDto.Sort)]
    public int Id { get; set; }

    [CrudField(CrudDto.Read | CrudDto.Create | CrudDto.Update | CrudDto.Filter | CrudDto.Sort, 
        Searchable = true, RequiredOnCreate = true)]
    public string Title { get; set; } = "";

    [CrudField(CrudDto.Read | CrudDto.Create | CrudDto.Update, Searchable = true)]
    public string? Description { get; set; }

    [CrudField(CrudDto.Read | CrudDto.Create | CrudDto.Update | CrudDto.Filter, 
        AllowedValues = "draft|published|archived", DefaultValue = "draft")]
    public string Status { get; set; } = "draft";

    [CrudField(CrudDto.Read | CrudDto.Create | CrudDto.Update | CrudDto.Filter | CrudDto.Sort)]
    public decimal Price { get; set; }

    [CrudField(CrudDto.Read | CrudDto.Filter)]
    public bool IsActive { get; set; }

    [CrudField(CrudDto.Read | CrudDto.Filter | CrudDto.Sort)]
    public DateTime CreatedAt { get; set; }

    [CrudField(CrudDto.Read | CrudDto.Filter)]
    public string? Email { get; set; }

    [CrudField(CrudDto.Read, ComputedExpression = "Title + ' - ' + Status")]
    public string? TitleWithStatus { get; set; }
}
