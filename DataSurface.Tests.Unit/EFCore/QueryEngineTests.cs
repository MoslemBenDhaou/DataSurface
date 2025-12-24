using DataSurface.Core.Contracts;
using DataSurface.Core.Enums;
using DataSurface.EFCore;
using DataSurface.EFCore.Contracts;
using FluentAssertions;
using Xunit;

namespace DataSurface.Tests.Unit.EFCore;

public class QueryEngineTests
{
    private readonly EfCrudQueryEngine _engine = new();

    private static ResourceContract CreateTestContract(
        IReadOnlyList<string>? filterableFields = null,
        IReadOnlyList<string>? sortableFields = null,
        IReadOnlyList<string>? searchableFields = null)
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
            ResourceKey: "TestEntity",
            Route: "test-entities",
            Backend: StorageBackend.EfCore,
            Key: new ResourceKeyContract("Id", FieldType.Int32),
            Query: new QueryContract(
                MaxPageSize: 100,
                FilterableFields: filterableFields ?? fields.Where(f => f.Filterable).Select(f => f.ApiName).ToList(),
                SortableFields: sortableFields ?? fields.Where(f => f.Sortable).Select(f => f.ApiName).ToList(),
                SearchableFields: searchableFields ?? fields.Where(f => f.Searchable).Select(f => f.ApiName).ToList(),
                DefaultSort: null
            ),
            Read: new ReadContract(Array.Empty<string>(), 1, Array.Empty<string>()),
            Fields: fields,
            Relations: Array.Empty<RelationContract>(),
            Operations: new Dictionary<CrudOperation, OperationContract>(),
            Security: new SecurityContract(new Dictionary<CrudOperation, string?>())
        );
    }

    private static FieldContract CreateField(
        string name,
        string apiName,
        FieldType type,
        bool filterable = false,
        bool sortable = false,
        bool searchable = false,
        bool nullable = false)
    {
        return new FieldContract(
            Name: name,
            ApiName: apiName,
            Type: type,
            Nullable: nullable,
            InRead: true,
            InCreate: true,
            InUpdate: true,
            Filterable: filterable,
            Sortable: sortable,
            Hidden: false,
            Immutable: false,
            Searchable: searchable,
            Computed: false,
            ComputedExpression: null,
            DefaultValue: null,
            Validation: new FieldValidationContract(false, null, null, null, null, null)
        );
    }

    public class TestEntity
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

    [Fact]
    public void Apply_WithDefaultSpec_AppliesPagination()
    {
        var contract = CreateTestContract();
        var data = Enumerable.Range(1, 50).Select(i => new TestEntity { Id = i, Title = $"Item {i}" }).AsQueryable();
        var spec = new QuerySpec(Page: 1, PageSize: 20);

        var result = _engine.Apply(data, contract, spec).ToList();

        result.Should().HaveCount(20);
        result.First().Id.Should().Be(1);
    }

    [Fact]
    public void Apply_WithPage2_SkipsFirstPage()
    {
        var contract = CreateTestContract();
        var data = Enumerable.Range(1, 50).Select(i => new TestEntity { Id = i, Title = $"Item {i}" }).AsQueryable();
        var spec = new QuerySpec(Page: 2, PageSize: 10);

        var result = _engine.Apply(data, contract, spec).ToList();

        result.Should().HaveCount(10);
        result.First().Id.Should().Be(11);
    }

    [Fact]
    public void Apply_WithEqFilter_FiltersCorrectly()
    {
        var contract = CreateTestContract();
        var data = new[]
        {
            new TestEntity { Id = 1, Title = "First", Status = "active" },
            new TestEntity { Id = 2, Title = "Second", Status = "inactive" },
            new TestEntity { Id = 3, Title = "Third", Status = "active" }
        }.AsQueryable();

        var spec = new QuerySpec(Filters: new Dictionary<string, string> { ["status"] = "eq:active" });

        var result = _engine.Apply(data, contract, spec).ToList();

        result.Should().HaveCount(2);
        result.Should().OnlyContain(e => e.Status == "active");
    }

    [Fact]
    public void Apply_WithNeqFilter_ExcludesMatches()
    {
        var contract = CreateTestContract();
        var data = new[]
        {
            new TestEntity { Id = 1, Status = "active" },
            new TestEntity { Id = 2, Status = "inactive" },
            new TestEntity { Id = 3, Status = "active" }
        }.AsQueryable();

        var spec = new QuerySpec(Filters: new Dictionary<string, string> { ["status"] = "neq:active" });

        var result = _engine.Apply(data, contract, spec).ToList();

        result.Should().HaveCount(1);
        result.First().Status.Should().Be("inactive");
    }

    [Fact]
    public void Apply_WithGtFilter_FiltersGreaterThan()
    {
        var contract = CreateTestContract();
        var data = new[]
        {
            new TestEntity { Id = 1, Price = 50 },
            new TestEntity { Id = 2, Price = 100 },
            new TestEntity { Id = 3, Price = 150 }
        }.AsQueryable();

        var spec = new QuerySpec(Filters: new Dictionary<string, string> { ["price"] = "gt:100" });

        var result = _engine.Apply(data, contract, spec).ToList();

        result.Should().HaveCount(1);
        result.First().Price.Should().Be(150);
    }

    [Fact]
    public void Apply_WithGteFilter_FiltersGreaterThanOrEqual()
    {
        var contract = CreateTestContract();
        var data = new[]
        {
            new TestEntity { Id = 1, Price = 50 },
            new TestEntity { Id = 2, Price = 100 },
            new TestEntity { Id = 3, Price = 150 }
        }.AsQueryable();

        var spec = new QuerySpec(Filters: new Dictionary<string, string> { ["price"] = "gte:100" });

        var result = _engine.Apply(data, contract, spec).ToList();

        result.Should().HaveCount(2);
    }

    [Fact]
    public void Apply_WithLtFilter_FiltersLessThan()
    {
        var contract = CreateTestContract();
        var data = new[]
        {
            new TestEntity { Id = 1, Price = 50 },
            new TestEntity { Id = 2, Price = 100 },
            new TestEntity { Id = 3, Price = 150 }
        }.AsQueryable();

        var spec = new QuerySpec(Filters: new Dictionary<string, string> { ["price"] = "lt:100" });

        var result = _engine.Apply(data, contract, spec).ToList();

        result.Should().HaveCount(1);
        result.First().Price.Should().Be(50);
    }

    [Fact]
    public void Apply_WithLteFilter_FiltersLessThanOrEqual()
    {
        var contract = CreateTestContract();
        var data = new[]
        {
            new TestEntity { Id = 1, Price = 50 },
            new TestEntity { Id = 2, Price = 100 },
            new TestEntity { Id = 3, Price = 150 }
        }.AsQueryable();

        var spec = new QuerySpec(Filters: new Dictionary<string, string> { ["price"] = "lte:100" });

        var result = _engine.Apply(data, contract, spec).ToList();

        result.Should().HaveCount(2);
    }

    [Fact]
    public void Apply_WithContainsFilter_FiltersStringContains()
    {
        var contract = CreateTestContract();
        var data = new[]
        {
            new TestEntity { Id = 1, Title = "Hello World" },
            new TestEntity { Id = 2, Title = "Goodbye World" },
            new TestEntity { Id = 3, Title = "Hello Universe" }
        }.AsQueryable();

        var spec = new QuerySpec(Filters: new Dictionary<string, string> { ["title"] = "contains:Hello" });

        var result = _engine.Apply(data, contract, spec).ToList();

        result.Should().HaveCount(2);
        result.Should().OnlyContain(e => e.Title.Contains("Hello"));
    }

    [Fact]
    public void Apply_WithStartsFilter_FiltersStringStartsWith()
    {
        var contract = CreateTestContract();
        var data = new[]
        {
            new TestEntity { Id = 1, Title = "Hello World" },
            new TestEntity { Id = 2, Title = "Goodbye World" },
            new TestEntity { Id = 3, Title = "Hello Universe" }
        }.AsQueryable();

        var spec = new QuerySpec(Filters: new Dictionary<string, string> { ["title"] = "starts:Hello" });

        var result = _engine.Apply(data, contract, spec).ToList();

        result.Should().HaveCount(2);
        result.Should().OnlyContain(e => e.Title.StartsWith("Hello"));
    }

    [Fact]
    public void Apply_WithEndsFilter_FiltersStringEndsWith()
    {
        var contract = CreateTestContract();
        var data = new[]
        {
            new TestEntity { Id = 1, Title = "Hello World" },
            new TestEntity { Id = 2, Title = "Goodbye World" },
            new TestEntity { Id = 3, Title = "Hello Universe" }
        }.AsQueryable();

        var spec = new QuerySpec(Filters: new Dictionary<string, string> { ["title"] = "ends:World" });

        var result = _engine.Apply(data, contract, spec).ToList();

        result.Should().HaveCount(2);
        result.Should().OnlyContain(e => e.Title.EndsWith("World"));
    }

    [Fact]
    public void Apply_WithIsNullTrueFilter_FiltersNullValues()
    {
        var contract = CreateTestContract();
        var data = new[]
        {
            new TestEntity { Id = 1, Email = "test@example.com" },
            new TestEntity { Id = 2, Email = null },
            new TestEntity { Id = 3, Email = "other@example.com" }
        }.AsQueryable();

        var spec = new QuerySpec(Filters: new Dictionary<string, string> { ["email"] = "isnull:true" });

        var result = _engine.Apply(data, contract, spec).ToList();

        result.Should().HaveCount(1);
        result.First().Email.Should().BeNull();
    }

    [Fact]
    public void Apply_WithIsNullFalseFilter_FiltersNonNullValues()
    {
        var contract = CreateTestContract();
        var data = new[]
        {
            new TestEntity { Id = 1, Email = "test@example.com" },
            new TestEntity { Id = 2, Email = null },
            new TestEntity { Id = 3, Email = "other@example.com" }
        }.AsQueryable();

        var spec = new QuerySpec(Filters: new Dictionary<string, string> { ["email"] = "isnull:false" });

        var result = _engine.Apply(data, contract, spec).ToList();

        result.Should().HaveCount(2);
        result.Should().OnlyContain(e => e.Email != null);
    }

    [Fact]
    public void Apply_WithInFilter_FiltersMultipleValues()
    {
        var contract = CreateTestContract();
        var data = new[]
        {
            new TestEntity { Id = 1, Status = "active" },
            new TestEntity { Id = 2, Status = "inactive" },
            new TestEntity { Id = 3, Status = "pending" },
            new TestEntity { Id = 4, Status = "archived" }
        }.AsQueryable();

        var spec = new QuerySpec(Filters: new Dictionary<string, string> { ["status"] = "in:active|pending" });

        var result = _engine.Apply(data, contract, spec).ToList();

        result.Should().HaveCount(2);
        result.Should().OnlyContain(e => e.Status == "active" || e.Status == "pending");
    }

    [Fact]
    public void Apply_WithSort_SortsAscending()
    {
        var contract = CreateTestContract();
        var data = new[]
        {
            new TestEntity { Id = 3, Title = "Charlie" },
            new TestEntity { Id = 1, Title = "Alpha" },
            new TestEntity { Id = 2, Title = "Beta" }
        }.AsQueryable();

        var spec = new QuerySpec(Sort: "title");

        var result = _engine.Apply(data, contract, spec).ToList();

        result[0].Title.Should().Be("Alpha");
        result[1].Title.Should().Be("Beta");
        result[2].Title.Should().Be("Charlie");
    }

    [Fact]
    public void Apply_WithDescendingSort_SortsDescending()
    {
        var contract = CreateTestContract();
        var data = new[]
        {
            new TestEntity { Id = 1, Title = "Alpha" },
            new TestEntity { Id = 2, Title = "Beta" },
            new TestEntity { Id = 3, Title = "Charlie" }
        }.AsQueryable();

        var spec = new QuerySpec(Sort: "-title");

        var result = _engine.Apply(data, contract, spec).ToList();

        result[0].Title.Should().Be("Charlie");
        result[1].Title.Should().Be("Beta");
        result[2].Title.Should().Be("Alpha");
    }

    [Fact]
    public void Apply_WithMultiSort_ProcessesMultipleSortKeys()
    {
        var contract = CreateTestContract();
        var data = new[]
        {
            new TestEntity { Id = 1, Status = "active", Title = "Zebra" },
            new TestEntity { Id = 2, Status = "active", Title = "Alpha" },
            new TestEntity { Id = 3, Status = "inactive", Title = "Beta" }
        }.AsQueryable();

        var spec = new QuerySpec(Sort: "status,title");

        var result = _engine.Apply(data, contract, spec).ToList();

        // Verifies multi-sort spec is accepted and returns results
        result.Should().HaveCount(3);
    }

    [Fact]
    public void Apply_WithSearch_SearchesAcrossSearchableFields()
    {
        var contract = CreateTestContract();
        var data = new[]
        {
            new TestEntity { Id = 1, Title = "Hello World", Description = "A greeting" },
            new TestEntity { Id = 2, Title = "Goodbye", Description = "A farewell with World" },
            new TestEntity { Id = 3, Title = "Other", Description = "Something else" }
        }.AsQueryable();

        var spec = new QuerySpec(Search: "World");

        var result = _engine.Apply(data, contract, spec).ToList();

        result.Should().HaveCount(2);
    }

    [Fact]
    public void Apply_WithSearch_NoSearchableFields_ReturnsAll()
    {
        var contract = CreateTestContract(searchableFields: Array.Empty<string>());
        var data = new[]
        {
            new TestEntity { Id = 1, Title = "Hello World" },
            new TestEntity { Id = 2, Title = "Goodbye" }
        }.AsQueryable();

        var spec = new QuerySpec(Search: "World");

        var result = _engine.Apply(data, contract, spec).ToList();

        result.Should().HaveCount(2);
    }

    [Fact]
    public void Apply_ClampsPageSizeToMaxPageSize()
    {
        var contract = CreateTestContract();
        var data = Enumerable.Range(1, 200).Select(i => new TestEntity { Id = i }).AsQueryable();
        var spec = new QuerySpec(PageSize: 500);

        var result = _engine.Apply(data, contract, spec).ToList();

        result.Should().HaveCount(100); // MaxPageSize is 100
    }

    [Fact]
    public void Apply_ClampsPageToMinimum1()
    {
        var contract = CreateTestContract();
        var data = Enumerable.Range(1, 50).Select(i => new TestEntity { Id = i }).AsQueryable();
        var spec = new QuerySpec(Page: 0, PageSize: 10);

        var result = _engine.Apply(data, contract, spec).ToList();

        result.First().Id.Should().Be(1);
    }

    [Fact]
    public void Apply_IgnoresFilterOnNonFilterableField()
    {
        var contract = CreateTestContract(filterableFields: new[] { "id" });
        var data = new[]
        {
            new TestEntity { Id = 1, Status = "active" },
            new TestEntity { Id = 2, Status = "inactive" }
        }.AsQueryable();

        var spec = new QuerySpec(Filters: new Dictionary<string, string> { ["status"] = "active" });

        var result = _engine.Apply(data, contract, spec).ToList();

        result.Should().HaveCount(2); // Filter ignored
    }

    [Fact]
    public void Apply_IgnoresSortOnNonSortableField()
    {
        var contract = CreateTestContract(sortableFields: new[] { "id" });
        var data = new[]
        {
            new TestEntity { Id = 2, Title = "Beta" },
            new TestEntity { Id = 1, Title = "Alpha" }
        }.AsQueryable();

        var spec = new QuerySpec(Sort: "title");

        var result = _engine.Apply(data, contract, spec).ToList();

        // Order unchanged since title is not sortable
        result[0].Id.Should().Be(2);
        result[1].Id.Should().Be(1);
    }
}
