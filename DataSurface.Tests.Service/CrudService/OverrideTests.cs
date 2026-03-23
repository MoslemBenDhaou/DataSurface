using System.Text.Json.Nodes;
using DataSurface.Core.Contracts;
using DataSurface.Core.Enums;
using DataSurface.EFCore.Context;
using DataSurface.EFCore.Contracts;
using DataSurface.EFCore.Exceptions;
using DataSurface.Tests.Service.Shared;
using DataSurface.Tests.Service.Shared.Builders;
using DataSurface.Tests.Service.Shared.Entities;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DataSurface.Tests.Service.CrudService;

/// <summary>
/// Tests for the CRUD override registry and override delegate invocation.
/// </summary>
public class OverrideTests : IDisposable
{
    private readonly CrudTestDbContext _db;

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

    public OverrideTests()
    {
        var options = new DbContextOptionsBuilder<CrudTestDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _db = new CrudTestDbContext(options);
        _db.Database.EnsureCreated();
    }

    public void Dispose() => _db.Dispose();

    // ────────────────────────────────────────────
    //  Override Registry unit tests
    // ────────────────────────────────────────────

    [Fact]
    public void Registry_TryGet_ReturnsFalseWhenEmpty()
    {
        var registry = new CrudOverrideRegistry();

        registry.TryGet<ListOverride>("SimpleItem", CrudOperation.List, out var handler)
            .Should().BeFalse();
        handler.Should().BeNull();
    }

    [Fact]
    public void Registry_Override_ThenTryGet_ReturnsRegisteredDelegate()
    {
        var registry = new CrudOverrideRegistry();
        ListOverride listFn = (c, spec, expand, ctx, ct) =>
            Task.FromResult(new PagedResult<JsonObject>(new List<JsonObject>(), 1, 10, 0));

        registry.Override("SimpleItem", CrudOperation.List, listFn);

        registry.TryGet<ListOverride>("SimpleItem", CrudOperation.List, out var handler)
            .Should().BeTrue();
        handler.Should().NotBeNull();
    }

    [Fact]
    public void Registry_IsCaseInsensitive()
    {
        var registry = new CrudOverrideRegistry();
        ListOverride listFn = (c, spec, expand, ctx, ct) =>
            Task.FromResult(new PagedResult<JsonObject>(new List<JsonObject>(), 1, 10, 0));

        registry.Override("SimpleItem", CrudOperation.List, listFn);

        registry.TryGet<ListOverride>("simpleitem", CrudOperation.List, out _)
            .Should().BeTrue();
        registry.TryGet<ListOverride>("SIMPLEITEM", CrudOperation.List, out _)
            .Should().BeTrue();
    }

    [Fact]
    public void Registry_DifferentOperations_Independent()
    {
        var registry = new CrudOverrideRegistry();
        ListOverride listFn = (c, spec, expand, ctx, ct) =>
            Task.FromResult(new PagedResult<JsonObject>(new List<JsonObject>(), 1, 10, 0));

        registry.Override("SimpleItem", CrudOperation.List, listFn);

        registry.TryGet<GetOverride>("SimpleItem", CrudOperation.Get, out _)
            .Should().BeFalse();
    }

    // ────────────────────────────────────────────
    //  List override via CrudService
    // ────────────────────────────────────────────

    [Fact]
    public async Task ListAsync_OverrideReturnsCustomResult()
    {
        var customItems = new List<JsonObject>
        {
            new JsonObject { ["id"] = 999, ["name"] = "Override Item" }
        };

        using var factory = new TestServiceFactory(_db, new[] { BuildContract() }, services =>
        {
            // Register after building
        });

        // Register override on the registry
        var registry = factory.Services.GetRequiredService<CrudOverrideRegistry>();
        registry.Override("SimpleItem", CrudOperation.List,
            (ListOverride)((c, spec, expand, ctx, ct) =>
                Task.FromResult(new PagedResult<JsonObject>(customItems, 1, 10, 1))));

        var result = await factory.CrudService.ListAsync("SimpleItem", new QuerySpec());

        result.Items.Should().HaveCount(1);
        result.Items[0]["name"]!.GetValue<string>().Should().Be("Override Item");
        result.Total.Should().Be(1);
    }

    // ────────────────────────────────────────────
    //  Get override via CrudService
    // ────────────────────────────────────────────

    [Fact]
    public async Task GetAsync_OverrideReturnsCustomResult()
    {
        using var factory = new TestServiceFactory(_db, new[] { BuildContract() });

        var registry = factory.Services.GetRequiredService<CrudOverrideRegistry>();
        registry.Override("SimpleItem", CrudOperation.Get,
            (GetOverride)((c, id, expand, ctx, ct) =>
                Task.FromResult<JsonObject?>(new JsonObject { ["id"] = 42, ["name"] = "Custom Get" })));

        var result = await factory.CrudService.GetAsync("SimpleItem", 42);

        result.Should().NotBeNull();
        result!["name"]!.GetValue<string>().Should().Be("Custom Get");
    }

    // ────────────────────────────────────────────
    //  Create override via CrudService
    // ────────────────────────────────────────────

    [Fact]
    public async Task CreateAsync_OverrideReturnsCustomResult()
    {
        using var factory = new TestServiceFactory(_db, new[] { BuildContract() });

        var registry = factory.Services.GetRequiredService<CrudOverrideRegistry>();
        registry.Override("SimpleItem", CrudOperation.Create,
            (CreateOverride)((c, body, ctx, ct) =>
                Task.FromResult(new JsonObject { ["id"] = 77, ["name"] = "Custom Created" })));

        var result = await factory.CrudService.CreateAsync("SimpleItem",
            new JsonObject { ["name"] = "Ignored" });

        result["name"]!.GetValue<string>().Should().Be("Custom Created");
    }

    // ────────────────────────────────────────────
    //  Delete override via CrudService
    // ────────────────────────────────────────────

    [Fact]
    public async Task DeleteAsync_OverrideBypassesDefaultLogic()
    {
        _db.SimpleItems.Add(new SimpleItem { Name = "OverrideDelete", Price = 5m });
        await _db.SaveChangesAsync();
        var id = _db.SimpleItems.First().Id;

        var deleteCalled = false;

        using var factory = new TestServiceFactory(_db, new[] { BuildContract() });

        var registry = factory.Services.GetRequiredService<CrudOverrideRegistry>();
        registry.Override("SimpleItem", CrudOperation.Delete,
            (DeleteOverride)((c, oid, spec, ctx, ct) =>
            {
                deleteCalled = true;
                return Task.CompletedTask;
            }));

        await factory.CrudService.DeleteAsync("SimpleItem", id);

        deleteCalled.Should().BeTrue();
        // Entity should still exist because override didn't actually delete
        _db.SimpleItems.Find(id).Should().NotBeNull();
    }

    // ────────────────────────────────────────────
    //  Without override, normal flow runs
    // ────────────────────────────────────────────

    [Fact]
    public async Task CreateAsync_NoOverride_PersistsNormally()
    {
        using var factory = new TestServiceFactory(_db, new[] { BuildContract() });
        // No override registered

        var result = await factory.CrudService.CreateAsync("SimpleItem",
            new JsonObject { ["name"] = "Normal", ["price"] = 10m });

        result.Should().NotBeNull();
        _db.SimpleItems.Should().ContainSingle(x => x.Name == "Normal");
    }
}
