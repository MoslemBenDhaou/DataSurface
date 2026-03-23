using System.Text.Json.Nodes;
using DataSurface.Core.Contracts;
using DataSurface.Core.Enums;
using DataSurface.EFCore.Contracts;
using DataSurface.EFCore.Interfaces;
using DataSurface.EFCore.Services;
using DataSurface.Tests.Service.Shared;
using DataSurface.Tests.Service.Shared.Builders;
using DataSurface.Tests.Service.Shared.Entities;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace DataSurface.Tests.Service.CrudService;

/// <summary>
/// Tests for <see cref="EfDataSurfaceBulkService"/>.
/// </summary>
public class BulkServiceTests : IDisposable
{
    private readonly TestServiceFactory _factory;
    private readonly CrudTestDbContext _db;
    private readonly EfDataSurfaceBulkService _bulk;

    private static ResourceContract BuildContract()
    {
        return new ResourceContractBuilder("SimpleItem", "simple-items")
            .Key("Id", FieldType.Int32)
            .WithField(new FieldBuilder("Id").OfType(FieldType.Int32).InRead().Filterable().Sortable().Build())
            .WithField(new FieldBuilder("Name").OfType(FieldType.String).ReadCreateUpdate().RequiredOnCreate().Build())
            .WithField(new FieldBuilder("Price").OfType(FieldType.Decimal).ReadCreateUpdate().Build())
            .EnableAllOperations()
            .Build();
    }

    public BulkServiceTests()
    {
        var options = new DbContextOptionsBuilder<CrudTestDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _db = new CrudTestDbContext(options);
        _db.Database.EnsureCreated();
        _factory = new TestServiceFactory(_db, new[] { BuildContract() });

        _bulk = new EfDataSurfaceBulkService(
            _db,
            _factory.CrudService,
            _factory.Contracts,
            NullLogger<EfDataSurfaceBulkService>.Instance);
    }

    public void Dispose() => _factory.Dispose();

    // ────────────────────────────────────────────
    //  Bulk Create
    // ────────────────────────────────────────────

    [Fact]
    public async Task BulkCreate_CreatesMultipleItems()
    {
        var spec = new BulkOperationSpec
        {
            Create = new[]
            {
                new JsonObject { ["name"] = "Bulk1", ["price"] = 10m },
                new JsonObject { ["name"] = "Bulk2", ["price"] = 20m },
                new JsonObject { ["name"] = "Bulk3", ["price"] = 30m }
            },
            UseTransaction = false
        };

        var result = await _bulk.ExecuteAsync("SimpleItem", spec);

        result.Success.Should().BeTrue();
        result.Created.Should().HaveCount(3);
        _db.SimpleItems.Count().Should().Be(3);
    }

    // ────────────────────────────────────────────
    //  Bulk Update
    // ────────────────────────────────────────────

    [Fact]
    public async Task BulkUpdate_UpdatesMultipleItems()
    {
        _db.SimpleItems.AddRange(
            new SimpleItem { Name = "A", Price = 1m },
            new SimpleItem { Name = "B", Price = 2m });
        await _db.SaveChangesAsync();
        var ids = _db.SimpleItems.Select(x => x.Id).ToList();

        var spec = new BulkOperationSpec
        {
            Update = new[]
            {
                new BulkUpdateItem { Id = ids[0], Patch = new JsonObject { ["name"] = "A-Updated" } },
                new BulkUpdateItem { Id = ids[1], Patch = new JsonObject { ["name"] = "B-Updated" } }
            },
            UseTransaction = false
        };

        var result = await _bulk.ExecuteAsync("SimpleItem", spec);

        result.Success.Should().BeTrue();
        result.Updated.Should().HaveCount(2);
        _db.SimpleItems.Find(ids[0])!.Name.Should().Be("A-Updated");
        _db.SimpleItems.Find(ids[1])!.Name.Should().Be("B-Updated");
    }

    // ────────────────────────────────────────────
    //  Bulk Delete
    // ────────────────────────────────────────────

    [Fact]
    public async Task BulkDelete_DeletesMultipleItems()
    {
        _db.SimpleItems.AddRange(
            new SimpleItem { Name = "X", Price = 1m },
            new SimpleItem { Name = "Y", Price = 2m },
            new SimpleItem { Name = "Z", Price = 3m });
        await _db.SaveChangesAsync();
        var ids = _db.SimpleItems.Select(x => (object)x.Id).ToList();

        var spec = new BulkOperationSpec
        {
            Delete = ids,
            UseTransaction = false
        };

        var result = await _bulk.ExecuteAsync("SimpleItem", spec);

        result.Success.Should().BeTrue();
        result.DeletedCount.Should().Be(3);
        _db.SimpleItems.Count().Should().Be(0);
    }

    // ────────────────────────────────────────────
    //  Mixed Operations
    // ────────────────────────────────────────────

    [Fact]
    public async Task BulkMixed_CreateUpdateDelete()
    {
        _db.SimpleItems.Add(new SimpleItem { Name = "Existing", Price = 5m });
        await _db.SaveChangesAsync();
        var existingId = _db.SimpleItems.First().Id;

        var spec = new BulkOperationSpec
        {
            Create = new[] { new JsonObject { ["name"] = "New", ["price"] = 99m } },
            Update = new[] { new BulkUpdateItem { Id = existingId, Patch = new JsonObject { ["name"] = "Modified" } } },
            Delete = Array.Empty<object>(),
            UseTransaction = false
        };

        var result = await _bulk.ExecuteAsync("SimpleItem", spec);

        result.Success.Should().BeTrue();
        result.Created.Should().HaveCount(1);
        result.Updated.Should().HaveCount(1);
        _db.SimpleItems.Count().Should().Be(2);
        _db.SimpleItems.Find(existingId)!.Name.Should().Be("Modified");
    }

    // ────────────────────────────────────────────
    //  Error Handling
    // ────────────────────────────────────────────

    [Fact]
    public async Task BulkCreate_InvalidItem_StopOnError_StopsProcessing()
    {
        var spec = new BulkOperationSpec
        {
            Create = new[]
            {
                new JsonObject { ["price"] = 10m }, // missing required "name"
                new JsonObject { ["name"] = "ShouldNotBeCreated", ["price"] = 20m }
            },
            StopOnError = true,
            UseTransaction = false
        };

        var result = await _bulk.ExecuteAsync("SimpleItem", spec);

        result.Success.Should().BeFalse();
        result.Errors.Should().HaveCount(1);
        result.Created.Should().BeEmpty();
        _db.SimpleItems.Count().Should().Be(0);
    }

    [Fact]
    public async Task BulkCreate_InvalidItem_ContinueOnError_ProcessesRemaining()
    {
        var spec = new BulkOperationSpec
        {
            Create = new[]
            {
                new JsonObject { ["price"] = 10m }, // missing required "name"
                new JsonObject { ["name"] = "Valid", ["price"] = 20m }
            },
            StopOnError = false,
            UseTransaction = false
        };

        var result = await _bulk.ExecuteAsync("SimpleItem", spec);

        result.Success.Should().BeFalse();
        result.Errors.Should().HaveCount(1);
        result.Created.Should().HaveCount(1);
        _db.SimpleItems.Should().ContainSingle(x => x.Name == "Valid");
    }

    // ────────────────────────────────────────────
    //  Empty Spec
    // ────────────────────────────────────────────

    [Fact]
    public async Task BulkEmpty_ReturnsSuccessWithZeroCounts()
    {
        var spec = new BulkOperationSpec { UseTransaction = false };

        var result = await _bulk.ExecuteAsync("SimpleItem", spec);

        result.Success.Should().BeTrue();
        result.Created.Should().BeEmpty();
        result.Updated.Should().BeEmpty();
        result.DeletedCount.Should().Be(0);
    }
}
