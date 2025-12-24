using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Order;
using DataSurface.Core.Contracts;
using DataSurface.Core.Enums;
using DataSurface.EFCore;
using DataSurface.EFCore.Contracts;

namespace DataSurface.Benchmarks;

[MemoryDiagnoser]
[Orderer(SummaryOrderPolicy.FastestToSlowest)]
[RankColumn]
public class QueryEngineBenchmarks
{
    private EfCrudQueryEngine _engine = null!;
    private ResourceContract _contract = null!;
    private IQueryable<BenchmarkEntity> _data = null!;

    [Params(100, 1000, 10000)]
    public int DataSize { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _engine = new EfCrudQueryEngine();
        _contract = CreateContract();
        _data = GenerateData(DataSize).AsQueryable();
    }

    private static ResourceContract CreateContract()
    {
        var fields = new List<FieldContract>
        {
            CreateField("Id", "id", FieldType.Int32, filterable: true, sortable: true),
            CreateField("Title", "title", FieldType.String, filterable: true, sortable: true, searchable: true),
            CreateField("Description", "description", FieldType.String, filterable: true, searchable: true),
            CreateField("Status", "status", FieldType.String, filterable: true),
            CreateField("Price", "price", FieldType.Decimal, filterable: true, sortable: true),
            CreateField("IsActive", "isActive", FieldType.Boolean, filterable: true),
            CreateField("CreatedAt", "createdAt", FieldType.DateTime, filterable: true, sortable: true),
            CreateField("Email", "email", FieldType.String, filterable: true, nullable: true)
        };

        return new ResourceContract(
            ResourceKey: "BenchmarkEntity",
            Route: "benchmark-entities",
            Backend: StorageBackend.EfCore,
            Key: new ResourceKeyContract("Id", FieldType.Int32),
            Query: new QueryContract(
                MaxPageSize: 100,
                FilterableFields: fields.Where(f => f.Filterable).Select(f => f.ApiName).ToList(),
                SortableFields: fields.Where(f => f.Sortable).Select(f => f.ApiName).ToList(),
                SearchableFields: fields.Where(f => f.Searchable).Select(f => f.ApiName).ToList(),
                DefaultSort: null
            ),
            Read: new ReadContract(Array.Empty<string>(), 1, Array.Empty<string>()),
            Fields: fields,
            Relations: Array.Empty<RelationContract>(),
            Operations: new Dictionary<CrudOperation, OperationContract>(),
            Security: new SecurityContract(new Dictionary<CrudOperation, string?>())
        );
    }

    private static FieldContract CreateField(string name, string apiName, FieldType type,
        bool filterable = false, bool sortable = false, bool searchable = false, bool nullable = false)
    {
        return new FieldContract(name, apiName, type, nullable, true, true, true,
            filterable, sortable, false, false, searchable, false, null, null,
            new FieldValidationContract(false, null, null, null, null, null));
    }

    private static List<BenchmarkEntity> GenerateData(int count)
    {
        var statuses = new[] { "active", "inactive", "pending", "archived" };
        var random = new Random(42);

        return Enumerable.Range(1, count).Select(i => new BenchmarkEntity
        {
            Id = i,
            Title = $"Title {i} with some text for search",
            Description = $"Description {i} containing various words",
            Status = statuses[random.Next(statuses.Length)],
            Price = (decimal)(random.NextDouble() * 1000),
            IsActive = random.Next(2) == 1,
            CreatedAt = DateTime.UtcNow.AddDays(-random.Next(365)),
            Email = i % 5 == 0 ? null : $"user{i}@example.com"
        }).ToList();
    }

    [Benchmark(Baseline = true)]
    public List<BenchmarkEntity> PaginationOnly()
    {
        var spec = new QuerySpec(Page: 1, PageSize: 20);
        return _engine.Apply(_data, _contract, spec).ToList();
    }

    [Benchmark]
    public List<BenchmarkEntity> SingleEqFilter()
    {
        var spec = new QuerySpec(Filters: new Dictionary<string, string> { ["status"] = "eq:active" });
        return _engine.Apply(_data, _contract, spec).ToList();
    }

    [Benchmark]
    public List<BenchmarkEntity> MultipleFilters()
    {
        var spec = new QuerySpec(Filters: new Dictionary<string, string>
        {
            ["status"] = "eq:active",
            ["isActive"] = "true",
            ["price"] = "gte:100"
        });
        return _engine.Apply(_data, _contract, spec).ToList();
    }

    [Benchmark]
    public List<BenchmarkEntity> IsNullFilter()
    {
        var spec = new QuerySpec(Filters: new Dictionary<string, string> { ["email"] = "isnull:true" });
        return _engine.Apply(_data, _contract, spec).ToList();
    }

    [Benchmark]
    public List<BenchmarkEntity> InFilter()
    {
        var spec = new QuerySpec(Filters: new Dictionary<string, string> { ["status"] = "in:active|pending" });
        return _engine.Apply(_data, _contract, spec).ToList();
    }

    [Benchmark]
    public List<BenchmarkEntity> ContainsFilter()
    {
        var spec = new QuerySpec(Filters: new Dictionary<string, string> { ["title"] = "contains:text" });
        return _engine.Apply(_data, _contract, spec).ToList();
    }

    [Benchmark]
    public List<BenchmarkEntity> FullTextSearch()
    {
        var spec = new QuerySpec(Search: "search");
        return _engine.Apply(_data, _contract, spec).ToList();
    }

    [Benchmark]
    public List<BenchmarkEntity> SingleSort()
    {
        var spec = new QuerySpec(Sort: "-createdAt");
        return _engine.Apply(_data, _contract, spec).ToList();
    }

    [Benchmark]
    public List<BenchmarkEntity> MultiSort()
    {
        var spec = new QuerySpec(Sort: "status,-price,title");
        return _engine.Apply(_data, _contract, spec).ToList();
    }

    [Benchmark]
    public List<BenchmarkEntity> FilterSortAndPaginate()
    {
        var spec = new QuerySpec(
            Page: 2,
            PageSize: 20,
            Sort: "-createdAt",
            Filters: new Dictionary<string, string>
            {
                ["status"] = "eq:active",
                ["price"] = "gte:50"
            }
        );
        return _engine.Apply(_data, _contract, spec).ToList();
    }

    [Benchmark]
    public List<BenchmarkEntity> FullTextSearchWithFilter()
    {
        var spec = new QuerySpec(
            Search: "text",
            Filters: new Dictionary<string, string> { ["status"] = "active" }
        );
        return _engine.Apply(_data, _contract, spec).ToList();
    }

    [Benchmark]
    public List<BenchmarkEntity> ComplexQuery()
    {
        var spec = new QuerySpec(
            Page: 1,
            PageSize: 50,
            Sort: "-price,title",
            Search: "search",
            Filters: new Dictionary<string, string>
            {
                ["status"] = "in:active|pending",
                ["isActive"] = "true",
                ["price"] = "gte:100",
                ["email"] = "isnull:false"
            }
        );
        return _engine.Apply(_data, _contract, spec).ToList();
    }
}

public class BenchmarkEntity
{
    public int Id { get; set; }
    public string Title { get; set; } = "";
    public string? Description { get; set; }
    public string Status { get; set; } = "";
    public decimal Price { get; set; }
    public bool IsActive { get; set; }
    public DateTime CreatedAt { get; set; }
    public string? Email { get; set; }
}
