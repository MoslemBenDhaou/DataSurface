using System.Text.Json.Nodes;
using DataSurface.Core.Contracts;
using DataSurface.Core.Enums;
using DataSurface.EFCore.Contracts;
using DataSurface.EFCore.Exceptions;
using DataSurface.Tests.Service.Shared;
using DataSurface.Tests.Service.Shared.Builders;
using DataSurface.Tests.Service.Shared.Entities;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace DataSurface.Tests.Service.CrudService;

/// <summary>
/// Behavioral tests for <see cref="EfDataSurfaceCrudService"/> covering
/// basic CRUD operations: List, Get, Create, Update, Delete.
/// </summary>
public class CrudServiceBasicTests : IDisposable
{
    private readonly TestServiceFactory _factory;
    private readonly CrudTestDbContext _db;

    private static ResourceContract BuildSimpleItemContract()
    {
        return new ResourceContractBuilder("SimpleItem", "simple-items")
            .Key("Id", FieldType.Int32)
            .MaxPageSize(100)
            .WithField(new FieldBuilder("Id").OfType(FieldType.Int32).InRead().Filterable().Sortable().Build())
            .WithField(new FieldBuilder("Name").OfType(FieldType.String).ReadCreateUpdate().Filterable().Sortable().Searchable().RequiredOnCreate().Build())
            .WithField(new FieldBuilder("Description").OfType(FieldType.String).Nullable().InRead().InCreate().InUpdate().Searchable().Build())
            .WithField(new FieldBuilder("Price").OfType(FieldType.Decimal).ReadCreateUpdate().Filterable().Sortable().Build())
            .WithField(new FieldBuilder("IsActive").OfType(FieldType.Boolean).InRead().InCreate().InUpdate().Filterable().Build())
            .EnableAllOperations()
            .Build();
    }

    public CrudServiceBasicTests()
    {
        var options = new DbContextOptionsBuilder<CrudTestDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        _db = new CrudTestDbContext(options);
        _db.Database.EnsureCreated();

        _factory = new TestServiceFactory(_db, new[] { BuildSimpleItemContract() });
    }

    public void Dispose() => _factory.Dispose();

    private async Task SeedItems(int count = 5)
    {
        for (int i = 1; i <= count; i++)
        {
            _db.SimpleItems.Add(new SimpleItem
            {
                Name = $"Item {i}",
                Description = i % 2 == 0 ? $"Desc {i}" : null,
                Price = i * 10m,
                IsActive = i % 2 == 0
            });
        }
        await _db.SaveChangesAsync();
    }

    // ────────────────────────────────────────────
    //  CREATE
    // ────────────────────────────────────────────

    [Fact]
    public async Task CreateAsync_ReturnsCreatedEntity()
    {
        var body = new JsonObject { ["name"] = "New Item", ["price"] = 42.5m };

        var result = await _factory.CrudService.CreateAsync("SimpleItem", body);

        result.Should().NotBeNull();
        result["name"]!.GetValue<string>().Should().Be("New Item");
        result["price"]!.GetValue<decimal>().Should().Be(42.5m);
        result["id"].Should().NotBeNull();
    }

    [Fact]
    public async Task CreateAsync_PersistsEntityInDatabase()
    {
        var body = new JsonObject { ["name"] = "Persisted", ["price"] = 99m };

        await _factory.CrudService.CreateAsync("SimpleItem", body);

        _db.SimpleItems.Should().ContainSingle(x => x.Name == "Persisted");
    }

    [Fact]
    public async Task CreateAsync_MissingRequiredFieldThrowsValidation()
    {
        var body = new JsonObject { ["price"] = 10m }; // missing "name"

        var act = () => _factory.CrudService.CreateAsync("SimpleItem", body);

        (await act.Should().ThrowAsync<CrudRequestValidationException>())
            .Which.Errors.Should().ContainKey("name");
    }

    [Fact]
    public async Task CreateAsync_UnknownFieldThrowsValidation()
    {
        var body = new JsonObject { ["name"] = "X", ["unknownField"] = "bad" };

        var act = () => _factory.CrudService.CreateAsync("SimpleItem", body);

        (await act.Should().ThrowAsync<CrudRequestValidationException>())
            .Which.Errors.Should().ContainKey("unknownField");
    }

    [Fact]
    public async Task CreateAsync_SetsDefaultIsActiveValue()
    {
        var body = new JsonObject { ["name"] = "Default Active", ["price"] = 1m, ["isActive"] = true };

        var result = await _factory.CrudService.CreateAsync("SimpleItem", body);

        result["isActive"]!.GetValue<bool>().Should().BeTrue();
    }

    // ────────────────────────────────────────────
    //  GET
    // ────────────────────────────────────────────

    [Fact]
    public async Task GetAsync_ExistingItemReturnsJson()
    {
        await SeedItems(1);
        var id = _db.SimpleItems.First().Id;

        var result = await _factory.CrudService.GetAsync("SimpleItem", id);

        result.Should().NotBeNull();
        result!["name"]!.GetValue<string>().Should().Be("Item 1");
    }

    [Fact]
    public async Task GetAsync_NonExistentIdReturnsNull()
    {
        var result = await _factory.CrudService.GetAsync("SimpleItem", 999);

        result.Should().BeNull();
    }

    [Fact]
    public async Task GetAsync_ReturnsOnlyReadFields()
    {
        await SeedItems(1);
        var id = _db.SimpleItems.First().Id;

        var result = await _factory.CrudService.GetAsync("SimpleItem", id);

        result.Should().NotBeNull();
        result!.Should().ContainKey("id");
        result.Should().ContainKey("name");
        result.Should().ContainKey("price");
        result.Should().ContainKey("isActive");
    }

    // ────────────────────────────────────────────
    //  LIST
    // ────────────────────────────────────────────

    [Fact]
    public async Task ListAsync_ReturnsPagedResult()
    {
        await SeedItems(10);

        var result = await _factory.CrudService.ListAsync("SimpleItem", new QuerySpec(Page: 1, PageSize: 5));

        result.Items.Should().HaveCount(5);
        result.Total.Should().Be(10);
        result.Page.Should().Be(1);
        result.PageSize.Should().Be(5);
    }

    [Fact]
    public async Task ListAsync_SecondPageReturnsRemainingItems()
    {
        await SeedItems(7);

        var result = await _factory.CrudService.ListAsync("SimpleItem", new QuerySpec(Page: 2, PageSize: 5));

        result.Items.Should().HaveCount(2);
        result.Total.Should().Be(7);
    }

    [Fact]
    public async Task ListAsync_EmptyDatabaseReturnsZeroItems()
    {
        var result = await _factory.CrudService.ListAsync("SimpleItem", new QuerySpec());

        result.Items.Should().BeEmpty();
        result.Total.Should().Be(0);
    }

    [Fact]
    public async Task ListAsync_AppliesFilter()
    {
        await SeedItems(10);
        var filters = new Dictionary<string, string> { ["isActive"] = "true" };

        var result = await _factory.CrudService.ListAsync("SimpleItem",
            new QuerySpec(Filters: filters));

        result.Items.Should().OnlyContain(j => j["isActive"]!.GetValue<bool>());
    }

    [Fact]
    public async Task ListAsync_AppliesSort()
    {
        await SeedItems(5);

        var result = await _factory.CrudService.ListAsync("SimpleItem",
            new QuerySpec(Sort: "-price"));

        var prices = result.Items.Select(j => j["price"]!.GetValue<decimal>()).ToList();
        prices.Should().BeInDescendingOrder();
    }

    [Fact]
    public async Task ListAsync_AppliesSearch()
    {
        await SeedItems(10);

        var result = await _factory.CrudService.ListAsync("SimpleItem",
            new QuerySpec(Search: "Item 1"));

        // "Item 1", "Item 10" both contain "Item 1"
        result.Items.Should().HaveCountGreaterOrEqualTo(1);
        result.Items.Should().OnlyContain(j => j["name"]!.GetValue<string>().Contains("Item 1"));
    }

    // ────────────────────────────────────────────
    //  UPDATE
    // ────────────────────────────────────────────

    [Fact]
    public async Task UpdateAsync_ModifiesEntity()
    {
        await SeedItems(1);
        var id = _db.SimpleItems.First().Id;
        var patch = new JsonObject { ["name"] = "Updated Name" };

        var result = await _factory.CrudService.UpdateAsync("SimpleItem", id, patch);

        result["name"]!.GetValue<string>().Should().Be("Updated Name");
        _db.SimpleItems.Find(id)!.Name.Should().Be("Updated Name");
    }

    [Fact]
    public async Task UpdateAsync_PartialPatchLeavesOtherFieldsUnchanged()
    {
        await SeedItems(1);
        var item = _db.SimpleItems.First();
        var originalPrice = item.Price;
        var patch = new JsonObject { ["name"] = "Patched" };

        await _factory.CrudService.UpdateAsync("SimpleItem", item.Id, patch);

        // Reload
        var updated = _db.SimpleItems.Find(item.Id)!;
        updated.Price.Should().Be(originalPrice);
    }

    [Fact]
    public async Task UpdateAsync_NonExistentIdThrowsNotFound()
    {
        var patch = new JsonObject { ["name"] = "X" };

        var act = () => _factory.CrudService.UpdateAsync("SimpleItem", 999, patch);

        await act.Should().ThrowAsync<CrudNotFoundException>();
    }

    [Fact]
    public async Task UpdateAsync_ImmutableFieldThrowsValidation()
    {
        await SeedItems(1);
        var id = _db.SimpleItems.First().Id;
        var patch = new JsonObject { ["id"] = 999 };

        var act = () => _factory.CrudService.UpdateAsync("SimpleItem", id, patch);

        await act.Should().ThrowAsync<CrudRequestValidationException>();
    }

    // ────────────────────────────────────────────
    //  DELETE
    // ────────────────────────────────────────────

    [Fact]
    public async Task DeleteAsync_RemovesEntityFromDatabase()
    {
        await SeedItems(1);
        var id = _db.SimpleItems.First().Id;

        await _factory.CrudService.DeleteAsync("SimpleItem", id);

        _db.SimpleItems.Find(id).Should().BeNull();
    }

    [Fact]
    public async Task DeleteAsync_NonExistentIdThrowsNotFound()
    {
        var act = () => _factory.CrudService.DeleteAsync("SimpleItem", 999);

        await act.Should().ThrowAsync<CrudNotFoundException>();
    }

    // ────────────────────────────────────────────
    //  Disabled Operations
    // ────────────────────────────────────────────

    [Fact]
    public async Task DisabledOperationThrowsInvalidOperation()
    {
        // Create a contract with Delete disabled
        var contract = new ResourceContractBuilder("SimpleItem", "simple-items")
            .Key("Id", FieldType.Int32)
            .WithField(new FieldBuilder("Id").OfType(FieldType.Int32).InRead().Build())
            .WithField(new FieldBuilder("Name").OfType(FieldType.String).ReadCreateUpdate().RequiredOnCreate().Build())
            .WithOperation(CrudOperation.Delete, enabled: false)
            .EnableAllOperations()
            .Build();

        var options = new DbContextOptionsBuilder<CrudTestDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        using var db = new CrudTestDbContext(options);
        db.Database.EnsureCreated();
        db.SimpleItems.Add(new SimpleItem { Name = "Test" });
        await db.SaveChangesAsync();

        using var factory = new TestServiceFactory(db, new[] { contract });

        var act = () => factory.CrudService.DeleteAsync("SimpleItem", 1);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*disabled*");
    }

    // ────────────────────────────────────────────
    //  Unknown Resource Key
    // ────────────────────────────────────────────

    [Fact]
    public async Task UnknownResourceKeyThrowsKeyNotFound()
    {
        var act = () => _factory.CrudService.ListAsync("NonExistent", new QuerySpec());

        await act.Should().ThrowAsync<KeyNotFoundException>();
    }

    // ────────────────────────────────────────────
    //  Create then Get roundtrip
    // ────────────────────────────────────────────

    [Fact]
    public async Task CreateThenGet_RoundtripPreservesData()
    {
        var body = new JsonObject
        {
            ["name"] = "Roundtrip",
            ["description"] = "Test desc",
            ["price"] = 55.55m,
            ["isActive"] = false
        };

        var created = await _factory.CrudService.CreateAsync("SimpleItem", body);
        var id = created["id"]!.GetValue<int>();

        var fetched = await _factory.CrudService.GetAsync("SimpleItem", id);

        fetched.Should().NotBeNull();
        fetched!["name"]!.GetValue<string>().Should().Be("Roundtrip");
        fetched["description"]!.GetValue<string>().Should().Be("Test desc");
        fetched["price"]!.GetValue<decimal>().Should().Be(55.55m);
        fetched["isActive"]!.GetValue<bool>().Should().BeFalse();
    }

    // ────────────────────────────────────────────
    //  Page Beyond Data
    // ────────────────────────────────────────────

    [Fact]
    public async Task List_PageBeyondData_ReturnsEmptyItemsWithCorrectTotal()
    {
        _db.SimpleItems.AddRange(
            new SimpleItem { Name = "A", Price = 1m },
            new SimpleItem { Name = "B", Price = 2m });
        await _db.SaveChangesAsync();

        var result = await _factory.CrudService.ListAsync("SimpleItem",
            new QuerySpec(Page: 10, PageSize: 10));

        result.Items.Should().BeEmpty();
        result.Total.Should().Be(2, "total should reflect actual count even for out-of-range pages");
    }

    [Fact]
    public async Task List_EmptyTable_ReturnsZeroItemsAndZeroTotal()
    {
        var result = await _factory.CrudService.ListAsync("SimpleItem", new QuerySpec());

        result.Items.Should().BeEmpty();
        result.Total.Should().Be(0);
    }
}
