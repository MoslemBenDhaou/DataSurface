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
/// Tests for soft delete behavior in the CRUD service.
/// </summary>
public class SoftDeleteTests : IDisposable
{
    private readonly TestServiceFactory _factory;
    private readonly CrudTestDbContext _db;

    private static ResourceContract BuildSoftDeleteContract()
    {
        return new ResourceContractBuilder("SoftDeleteItem", "soft-items")
            .Key("Id", FieldType.Int32)
            .WithField(new FieldBuilder("Id").OfType(FieldType.Int32).InRead().Filterable().Sortable().Build())
            .WithField(new FieldBuilder("Name").OfType(FieldType.String).ReadCreateUpdate().RequiredOnCreate().Build())
            .WithField(new FieldBuilder("IsDeleted").OfType(FieldType.Boolean).InRead().Build())
            .EnableAllOperations()
            .Build();
    }

    public SoftDeleteTests()
    {
        var options = new DbContextOptionsBuilder<CrudTestDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _db = new CrudTestDbContext(options);
        _db.Database.EnsureCreated();
        _factory = new TestServiceFactory(_db, new[] { BuildSoftDeleteContract() });
    }

    public void Dispose() => _factory.Dispose();

    [Fact]
    public async Task DeleteAsync_SoftDeleteSetsIsDeletedTrue()
    {
        _db.SoftDeleteItems.Add(new SoftDeleteItem { Name = "ToSoftDelete" });
        await _db.SaveChangesAsync();
        var id = _db.SoftDeleteItems.First().Id;

        await _factory.CrudService.DeleteAsync("SoftDeleteItem", id);

        var entity = _db.SoftDeleteItems.Find(id);
        entity.Should().NotBeNull();
        entity!.IsDeleted.Should().BeTrue();
    }

    [Fact]
    public async Task DeleteAsync_SoftDeleteDoesNotRemoveFromDb()
    {
        _db.SoftDeleteItems.Add(new SoftDeleteItem { Name = "Kept" });
        await _db.SaveChangesAsync();
        var id = _db.SoftDeleteItems.First().Id;

        await _factory.CrudService.DeleteAsync("SoftDeleteItem", id);

        _db.SoftDeleteItems.Count().Should().Be(1);
    }

    [Fact]
    public async Task DeleteAsync_HardDeleteRemovesFromDb()
    {
        _db.SoftDeleteItems.Add(new SoftDeleteItem { Name = "ToHardDelete" });
        await _db.SaveChangesAsync();
        var id = _db.SoftDeleteItems.First().Id;

        await _factory.CrudService.DeleteAsync("SoftDeleteItem", id,
            new CrudDeleteSpec(HardDelete: true));

        _db.SoftDeleteItems.Find(id).Should().BeNull();
    }

    [Fact]
    public async Task DeleteAsync_HardDeleteOnSoftDeleteEntityRemovesCompletely()
    {
        _db.SoftDeleteItems.Add(new SoftDeleteItem { Name = "Gone" });
        await _db.SaveChangesAsync();
        var id = _db.SoftDeleteItems.First().Id;

        await _factory.CrudService.DeleteAsync("SoftDeleteItem", id,
            new CrudDeleteSpec(HardDelete: true));

        _db.SoftDeleteItems.Count().Should().Be(0);
    }

    [Fact]
    public async Task DeleteAsync_NonExistentSoftDeleteThrowsNotFound()
    {
        var act = () => _factory.CrudService.DeleteAsync("SoftDeleteItem", 999);

        await act.Should().ThrowAsync<CrudNotFoundException>();
    }

    // ────────────────────────────────────────────
    //  B12: soft-deleted rows are hidden from reads by default
    // ────────────────────────────────────────────

    [Fact]
    public async Task ListAndGet_ExcludeSoftDeletedItems()
    {
        _db.SoftDeleteItems.AddRange(
            new SoftDeleteItem { Name = "Active" },
            new SoftDeleteItem { Name = "Removed" });
        await _db.SaveChangesAsync();
        var removedId = _db.SoftDeleteItems.First(x => x.Name == "Removed").Id;

        await _factory.CrudService.DeleteAsync("SoftDeleteItem", removedId); // soft delete

        var list = await _factory.CrudService.ListAsync("SoftDeleteItem", new QuerySpec());
        list.Items.Should().HaveCount(1);
        list.Total.Should().Be(1);

        var got = await _factory.CrudService.GetAsync("SoftDeleteItem", removedId);
        got.Should().BeNull();
    }
}
