using DataSurface.Core.Contracts;
using DataSurface.Core.Enums;
using DataSurface.EFCore;
using DataSurface.EFCore.Contracts;
using DataSurface.EFCore.Exceptions;
using DataSurface.Tests.Service.Shared.Builders;
using FluentAssertions;
using Xunit;

namespace DataSurface.Tests.Service.QueryEngine;

/// <summary>
/// Edge-case tests for <see cref="EfCrudQueryEngine"/>:
/// paging boundary conditions, filter parse edge cases, sort composition, search null safety.
/// </summary>
public class QueryEngineEdgeCaseTests
{
    private readonly EfCrudQueryEngine _engine = new();

    // ── Test entity ──
    private class Item
    {
        public int Id { get; set; }
        public string Name { get; set; } = "";
        public string? Description { get; set; }
        public decimal Price { get; set; }
        public int? Quantity { get; set; }
        public DateTime CreatedAt { get; set; }
        public bool IsActive { get; set; }
        public Guid ExternalId { get; set; }
    }

    private static ResourceContract MakeContract(int maxPageSize = 100)
    {
        return new ResourceContractBuilder("Item", "items")
            .MaxPageSize(maxPageSize)
            .WithField(new FieldBuilder("Id").OfType(FieldType.Int32).InRead().Filterable().Sortable().Build())
            .WithField(new FieldBuilder("Name").OfType(FieldType.String).ReadCreateUpdate().Filterable().Sortable().Searchable().Build())
            .WithField(new FieldBuilder("Description").OfType(FieldType.String).Nullable().InRead().Searchable().Build())
            .WithField(new FieldBuilder("Price").OfType(FieldType.Decimal).ReadCreateUpdate().Filterable().Sortable().Build())
            .WithField(new FieldBuilder("Quantity").OfType(FieldType.Int32).Nullable().InRead().Filterable().Sortable().Build())
            .WithField(new FieldBuilder("CreatedAt").OfType(FieldType.DateTime).InRead().Filterable().Sortable().Build())
            .WithField(new FieldBuilder("IsActive").OfType(FieldType.Boolean).InRead().Filterable().Build())
            .WithField(new FieldBuilder("ExternalId").OfType(FieldType.Guid).InRead().Filterable().Build())
            .EnableAllOperations()
            .Build();
    }

    private static IQueryable<Item> SeedData(int count = 20)
    {
        return Enumerable.Range(1, count).Select(i => new Item
        {
            Id = i,
            Name = $"Item {i}",
            Description = i % 3 == 0 ? null : $"Description for item {i}",
            Price = i * 10.5m,
            Quantity = i % 4 == 0 ? null : i * 5,
            CreatedAt = new DateTime(2024, 1, 1).AddDays(i),
            IsActive = i % 2 == 0,
            ExternalId = Guid.NewGuid()
        }).AsQueryable();
    }

    // ────────────────────────────────────────────
    //  Paging Boundary Conditions
    // ────────────────────────────────────────────

    [Fact]
    public void Apply_PageZeroClampedToOne()
    {
        var data = SeedData();
        var contract = MakeContract();
        var spec = new QuerySpec(Page: 0, PageSize: 5);

        var result = _engine.Apply(data, contract, spec).ToList();

        result.Should().HaveCount(5);
        result.First().Id.Should().Be(1);
    }

    [Fact]
    public void Apply_NegativePageClampedToOne()
    {
        var data = SeedData();
        var contract = MakeContract();
        var spec = new QuerySpec(Page: -10, PageSize: 5);

        var result = _engine.Apply(data, contract, spec).ToList();

        result.First().Id.Should().Be(1);
    }

    [Fact]
    public void Apply_PageSizeZeroClampedToOne()
    {
        var data = SeedData();
        var contract = MakeContract();
        var spec = new QuerySpec(Page: 1, PageSize: 0);

        var result = _engine.Apply(data, contract, spec).ToList();

        result.Should().HaveCount(1);
    }

    [Fact]
    public void Apply_PageSizeExceedingMaxIsClamped()
    {
        var data = SeedData(200);
        var contract = MakeContract(maxPageSize: 10);
        var spec = new QuerySpec(Page: 1, PageSize: 999);

        var result = _engine.Apply(data, contract, spec).ToList();

        result.Should().HaveCount(10);
    }

    [Fact]
    public void Apply_PageBeyondDataReturnsEmpty()
    {
        var data = SeedData(5);
        var contract = MakeContract();
        var spec = new QuerySpec(Page: 100, PageSize: 10);

        var result = _engine.Apply(data, contract, spec).ToList();

        result.Should().BeEmpty();
    }

    [Fact]
    public void Apply_EmptyDataReturnsEmpty()
    {
        var data = Enumerable.Empty<Item>().AsQueryable();
        var contract = MakeContract();
        var spec = new QuerySpec(Page: 1, PageSize: 10);

        var result = _engine.Apply(data, contract, spec).ToList();

        result.Should().BeEmpty();
    }

    // ────────────────────────────────────────────
    //  Filter: Non-Allowlisted Fields Ignored
    // ────────────────────────────────────────────

    [Fact]
    public void Apply_FilterOnNonAllowlistedFieldIsIgnored()
    {
        var data = SeedData();
        var contract = MakeContract();
        var filters = new Dictionary<string, string> { ["nonExistent"] = "anything" };
        var spec = new QuerySpec(Filters: filters);

        var result = _engine.Apply(data, contract, spec).ToList();

        result.Should().HaveCount(20);
    }

    // ────────────────────────────────────────────
    //  Filter: Boolean
    // ────────────────────────────────────────────

    [Fact]
    public void Apply_BooleanFilterWorks()
    {
        var data = SeedData();
        var contract = MakeContract();
        var filters = new Dictionary<string, string> { ["isActive"] = "true" };
        var spec = new QuerySpec(Filters: filters);

        var result = _engine.Apply(data, contract, spec).ToList();

        result.Should().OnlyContain(x => x.IsActive);
    }

    // ────────────────────────────────────────────
    //  Filter: IsNull on Nullable<int>
    // ────────────────────────────────────────────

    [Fact]
    public void Apply_IsNullTrueOnNullableIntFiltersNulls()
    {
        var data = SeedData();
        var contract = MakeContract();
        var filters = new Dictionary<string, string> { ["quantity"] = "isnull:true" };
        var spec = new QuerySpec(Filters: filters);

        var result = _engine.Apply(data, contract, spec).ToList();

        result.Should().OnlyContain(x => x.Quantity == null);
    }

    [Fact]
    public void Apply_IsNullFalseOnNullableIntFiltersNonNulls()
    {
        var data = SeedData();
        var contract = MakeContract();
        var filters = new Dictionary<string, string> { ["quantity"] = "isnull:false" };
        var spec = new QuerySpec(Filters: filters);

        var result = _engine.Apply(data, contract, spec).ToList();

        result.Should().OnlyContain(x => x.Quantity != null);
    }

    // ────────────────────────────────────────────
    //  Filter: In operator with multiple values
    // ────────────────────────────────────────────

    [Fact]
    public void Apply_InOperatorWithMultipleIds()
    {
        var data = SeedData();
        var contract = MakeContract();
        var filters = new Dictionary<string, string> { ["id"] = "in:1|3|5" };
        var spec = new QuerySpec(Filters: filters);

        var result = _engine.Apply(data, contract, spec).ToList();

        result.Select(x => x.Id).Should().BeEquivalentTo(new[] { 1, 3, 5 });
    }

    [Fact]
    public void Apply_InOperatorWithSingleValue()
    {
        var data = SeedData();
        var contract = MakeContract();
        var filters = new Dictionary<string, string> { ["id"] = "in:7" };
        var spec = new QuerySpec(Filters: filters);

        var result = _engine.Apply(data, contract, spec).ToList();

        result.Should().ContainSingle().Which.Id.Should().Be(7);
    }

    // ────────────────────────────────────────────
    //  Filter: Invalid Value Throws Validation Error
    // ────────────────────────────────────────────

    [Fact]
    public void Apply_InvalidFilterValueThrowsValidationException()
    {
        var data = SeedData();
        var contract = MakeContract();
        var filters = new Dictionary<string, string> { ["price"] = "gt:notanumber" };
        var spec = new QuerySpec(Filters: filters);

        var act = () => _engine.Apply(data, contract, spec).ToList();

        act.Should().Throw<CrudRequestValidationException>();
    }

    // ────────────────────────────────────────────
    //  Filter: Combined Filters (AND semantics)
    // ────────────────────────────────────────────

    [Fact]
    public void Apply_MultipleFiltersApplyWithAndSemantics()
    {
        var data = SeedData();
        var contract = MakeContract();
        var filters = new Dictionary<string, string>
        {
            ["isActive"] = "true",
            ["price"] = "gte:100"
        };
        var spec = new QuerySpec(Filters: filters);

        var result = _engine.Apply(data, contract, spec).ToList();

        result.Should().OnlyContain(x => x.IsActive && x.Price >= 100);
    }

    // ────────────────────────────────────────────
    //  Filter: String operators
    // ────────────────────────────────────────────

    [Fact]
    public void Apply_ContainsFilterOnString()
    {
        var data = SeedData();
        var contract = MakeContract();
        var filters = new Dictionary<string, string> { ["name"] = "contains:Item 1" };
        var spec = new QuerySpec(Filters: filters);

        var result = _engine.Apply(data, contract, spec).ToList();

        result.Should().OnlyContain(x => x.Name.Contains("Item 1"));
    }

    [Fact]
    public void Apply_StartsWithFilterOnString()
    {
        var data = SeedData();
        var contract = MakeContract();
        var filters = new Dictionary<string, string> { ["name"] = "starts:Item 2" };
        var spec = new QuerySpec(Filters: filters);

        var result = _engine.Apply(data, contract, spec).ToList();

        result.Should().OnlyContain(x => x.Name.StartsWith("Item 2"));
    }

    [Fact]
    public void Apply_EndsWithFilterOnString()
    {
        var items = new[]
        {
            new Item { Id = 1, Name = "Alpha" },
            new Item { Id = 2, Name = "Beta" },
            new Item { Id = 3, Name = "Gamma" }
        }.AsQueryable();
        var contract = MakeContract();
        var filters = new Dictionary<string, string> { ["name"] = "ends:ta" };
        var spec = new QuerySpec(Filters: filters);

        var result = _engine.Apply(items, contract, spec).ToList();

        result.Should().ContainSingle().Which.Name.Should().Be("Beta");
    }

    // ────────────────────────────────────────────
    //  Sort: Multi-field sort
    // ────────────────────────────────────────────

    [Fact]
    public void Apply_MultiFieldSortAscAndDesc()
    {
        var items = new[]
        {
            new Item { Id = 1, Name = "B", Price = 20 },
            new Item { Id = 2, Name = "A", Price = 10 },
            new Item { Id = 3, Name = "A", Price = 30 },
            new Item { Id = 4, Name = "B", Price = 10 },
        }.AsQueryable();
        var contract = MakeContract();
        var spec = new QuerySpec(Sort: "name,-price");

        var result = _engine.Apply(items, contract, spec).ToList();

        result.Select(x => x.Id).Should().ContainInOrder(3, 2, 1, 4);
    }

    [Fact]
    public void Apply_SortOnNonAllowlistedFieldIsIgnored()
    {
        var data = SeedData();
        var contract = MakeContract();
        var spec = new QuerySpec(Sort: "nonExistent");

        // Should not throw and return default order
        var result = _engine.Apply(data, contract, spec).ToList();

        result.Should().HaveCount(20);
    }

    [Fact]
    public void Apply_DescendingSortPrefixWorks()
    {
        var data = SeedData(5);
        var contract = MakeContract();
        var spec = new QuerySpec(Sort: "-id");

        var result = _engine.Apply(data, contract, spec).ToList();

        result.Select(x => x.Id).Should().BeInDescendingOrder();
    }

    // ────────────────────────────────────────────
    //  Search: null description safety
    // ────────────────────────────────────────────

    [Fact]
    public void Apply_SearchSkipsNullStringFields()
    {
        // Items where Description is null for i%3==0
        var data = SeedData();
        var contract = MakeContract();
        var spec = new QuerySpec(Search: "Description for item 1");

        // Should not throw on null descriptions
        var result = _engine.Apply(data, contract, spec).ToList();

        result.Should().NotBeEmpty();
    }

    [Fact]
    public void Apply_SearchWithNoSearchableFieldsReturnsAll()
    {
        var data = SeedData();
        // Build a contract with no searchable fields
        var contract = new ResourceContractBuilder("Item", "items")
            .WithField(new FieldBuilder("Id").OfType(FieldType.Int32).InRead().Filterable().Build())
            .WithField(new FieldBuilder("Name").OfType(FieldType.String).InRead().Build())
            .EnableAllOperations()
            .Build();
        var spec = new QuerySpec(Search: "anything");

        var result = _engine.Apply(data, contract, spec).ToList();

        result.Should().HaveCount(20);
    }

    [Fact]
    public void Apply_SearchMatchesAcrossMultipleFields()
    {
        var items = new[]
        {
            new Item { Id = 1, Name = "Alpha", Description = "Bravo" },
            new Item { Id = 2, Name = "Charlie", Description = "Alpha stuff" },
            new Item { Id = 3, Name = "Delta", Description = "Echo" },
        }.AsQueryable();
        var contract = MakeContract();
        var spec = new QuerySpec(Search: "Alpha");

        var result = _engine.Apply(items, contract, spec).ToList();

        result.Select(x => x.Id).Should().BeEquivalentTo(new[] { 1, 2 });
    }

    // ────────────────────────────────────────────
    //  Filter: DateTime
    // ────────────────────────────────────────────

    [Fact]
    public void Apply_DateTimeFilterWorks()
    {
        var data = SeedData();
        var contract = MakeContract();
        var filters = new Dictionary<string, string> { ["createdAt"] = "gte:2024-01-10" };
        var spec = new QuerySpec(Filters: filters);

        var result = _engine.Apply(data, contract, spec).ToList();

        result.Should().OnlyContain(x => x.CreatedAt >= new DateTime(2024, 1, 10));
    }

    // ────────────────────────────────────────────
    //  Filter: Equality default (no op prefix)
    // ────────────────────────────────────────────

    [Fact]
    public void Apply_PlainValueDefaultsToEquality()
    {
        var data = SeedData();
        var contract = MakeContract();
        var filters = new Dictionary<string, string> { ["id"] = "5" };
        var spec = new QuerySpec(Filters: filters);

        var result = _engine.Apply(data, contract, spec).ToList();

        result.Should().ContainSingle().Which.Id.Should().Be(5);
    }

    // ────────────────────────────────────────────
    //  ApplyFiltersAndSort (no paging)
    // ────────────────────────────────────────────

    [Fact]
    public void ApplyFiltersAndSort_ReturnsAllMatchingWithoutPaging()
    {
        var data = SeedData(50);
        var contract = MakeContract(maxPageSize: 10);
        var filters = new Dictionary<string, string> { ["isActive"] = "true" };
        var spec = new QuerySpec(Filters: filters);

        var result = _engine.ApplyFiltersAndSort(data, contract, spec).ToList();

        // Should return ALL matching items, not limited by maxPageSize
        result.Should().HaveCount(25);
    }

    // ────────────────────────────────────────────
    //  Filter: neq operator
    // ────────────────────────────────────────────

    [Fact]
    public void Apply_NeqFilterExcludesMatchingRecords()
    {
        var data = SeedData(5);
        var contract = MakeContract();
        var filters = new Dictionary<string, string> { ["id"] = "neq:3" };
        var spec = new QuerySpec(Filters: filters);

        var result = _engine.Apply(data, contract, spec).ToList();

        result.Should().HaveCount(4);
        result.Should().NotContain(x => x.Id == 3);
    }
}
