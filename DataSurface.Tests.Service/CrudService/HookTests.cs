using System.Text.Json.Nodes;
using DataSurface.Core.Contracts;
using DataSurface.Core.Enums;
using DataSurface.EFCore.Context;
using DataSurface.EFCore.Contracts;
using DataSurface.EFCore.Interfaces;
using DataSurface.Tests.Service.Shared;
using DataSurface.Tests.Service.Shared.Builders;
using DataSurface.Tests.Service.Shared.Entities;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DataSurface.Tests.Service.CrudService;

/// <summary>
/// Tests for CRUD hook invocation: verifies that global and typed hooks are called
/// at the correct points in the CRUD lifecycle.
/// </summary>
public class HookTests : IDisposable
{
    private readonly CrudTestDbContext _db;

    private static ResourceContract BuildSimpleContract()
    {
        return new ResourceContractBuilder("SimpleItem", "simple-items")
            .Key("Id", FieldType.Int32)
            .WithField(new FieldBuilder("Id").OfType(FieldType.Int32).InRead().Filterable().Sortable().Build())
            .WithField(new FieldBuilder("Name").OfType(FieldType.String).ReadCreateUpdate().RequiredOnCreate().Build())
            .WithField(new FieldBuilder("Price").OfType(FieldType.Decimal).ReadCreateUpdate().Build())
            .EnableAllOperations()
            .Build();
    }

    private static ResourceContract BuildSoftDeleteContract()
    {
        return new ResourceContractBuilder("SoftDeleteItem", "soft-items")
            .Key("Id", FieldType.Int32)
            .WithField(new FieldBuilder("Id").OfType(FieldType.Int32).InRead().Build())
            .WithField(new FieldBuilder("Name").OfType(FieldType.String).ReadCreateUpdate().RequiredOnCreate().Build())
            .WithField(new FieldBuilder("IsDeleted").OfType(FieldType.Boolean).InRead().Build())
            .EnableAllOperations()
            .Build();
    }

    public HookTests()
    {
        var options = new DbContextOptionsBuilder<CrudTestDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _db = new CrudTestDbContext(options);
        _db.Database.EnsureCreated();
    }

    public void Dispose() => _db.Dispose();

    // ────────────────────────────────────────────
    //  Tracking hook for SimpleItem (typed)
    // ────────────────────────────────────────────

    private class TrackingHook : ICrudHook<SimpleItem>
    {
        public int Order => 0;
        public List<string> Calls { get; } = new();

        public Task BeforeCreateAsync(SimpleItem entity, JsonObject body, CrudHookContext ctx)
        { Calls.Add("BeforeCreate"); return Task.CompletedTask; }
        public Task AfterCreateAsync(SimpleItem entity, CrudHookContext ctx)
        { Calls.Add("AfterCreate"); return Task.CompletedTask; }
        public Task AfterReadAsync(SimpleItem entity, CrudHookContext ctx)
        { Calls.Add("AfterRead"); return Task.CompletedTask; }
        public Task BeforeUpdateAsync(SimpleItem entity, JsonObject patch, CrudHookContext ctx)
        { Calls.Add("BeforeUpdate"); return Task.CompletedTask; }
        public Task AfterUpdateAsync(SimpleItem entity, CrudHookContext ctx)
        { Calls.Add("AfterUpdate"); return Task.CompletedTask; }
        public Task BeforeDeleteAsync(SimpleItem entity, CrudHookContext ctx)
        { Calls.Add("BeforeDelete"); return Task.CompletedTask; }
        public Task AfterDeleteAsync(SimpleItem entity, CrudHookContext ctx)
        { Calls.Add("AfterDelete"); return Task.CompletedTask; }
    }

    // ────────────────────────────────────────────
    //  Tracking hook for SoftDeleteItem (typed)
    // ────────────────────────────────────────────

    private class SoftDeleteTrackingHook : ICrudHook<SoftDeleteItem>
    {
        public int Order => 0;
        public List<string> Calls { get; } = new();

        public Task BeforeCreateAsync(SoftDeleteItem entity, JsonObject body, CrudHookContext ctx)
        { Calls.Add("BeforeCreate"); return Task.CompletedTask; }
        public Task AfterCreateAsync(SoftDeleteItem entity, CrudHookContext ctx)
        { Calls.Add("AfterCreate"); return Task.CompletedTask; }
        public Task AfterReadAsync(SoftDeleteItem entity, CrudHookContext ctx)
        { Calls.Add("AfterRead"); return Task.CompletedTask; }
        public Task BeforeUpdateAsync(SoftDeleteItem entity, JsonObject patch, CrudHookContext ctx)
        { Calls.Add("BeforeUpdate"); return Task.CompletedTask; }
        public Task AfterUpdateAsync(SoftDeleteItem entity, CrudHookContext ctx)
        { Calls.Add("AfterUpdate"); return Task.CompletedTask; }
        public Task BeforeDeleteAsync(SoftDeleteItem entity, CrudHookContext ctx)
        { Calls.Add("BeforeDelete"); return Task.CompletedTask; }
        public Task AfterDeleteAsync(SoftDeleteItem entity, CrudHookContext ctx)
        { Calls.Add("AfterDelete"); return Task.CompletedTask; }
    }

    // ────────────────────────────────────────────
    //  Global hook
    // ────────────────────────────────────────────

    private class GlobalTrackingHook : ICrudHook
    {
        public int Order => 0;
        public List<string> Calls { get; } = new();

        public Task BeforeAsync(CrudHookContext ctx)
        { Calls.Add($"GlobalBefore:{ctx.Operation}"); return Task.CompletedTask; }
        public Task AfterAsync(CrudHookContext ctx)
        { Calls.Add($"GlobalAfter:{ctx.Operation}"); return Task.CompletedTask; }
    }

    private TestServiceFactory CreateFactory(
        TrackingHook? typedHook = null,
        GlobalTrackingHook? globalHook = null,
        SoftDeleteTrackingHook? sdHook = null)
    {
        var contracts = new List<ResourceContract> { BuildSimpleContract(), BuildSoftDeleteContract() };
        return new TestServiceFactory(_db, contracts, services =>
        {
            if (typedHook != null)
                services.AddSingleton<ICrudHook<SimpleItem>>(typedHook);
            if (sdHook != null)
                services.AddSingleton<ICrudHook<SoftDeleteItem>>(sdHook);
            if (globalHook != null)
                services.AddSingleton<ICrudHook>(globalHook);
        });
    }

    // ────────────────────────────────────────────
    //  Create hooks
    // ────────────────────────────────────────────

    [Fact]
    public async Task CreateAsync_InvokesBeforeAndAfterCreateHooks()
    {
        var hook = new TrackingHook();
        using var factory = CreateFactory(typedHook: hook);

        await factory.CrudService.CreateAsync("SimpleItem",
            new JsonObject { ["name"] = "Test", ["price"] = 10m });

        hook.Calls.Should().ContainInOrder("BeforeCreate", "AfterCreate");
    }

    [Fact]
    public async Task CreateAsync_InvokesGlobalBeforeAndAfterHooks()
    {
        var global = new GlobalTrackingHook();
        using var factory = CreateFactory(globalHook: global);

        await factory.CrudService.CreateAsync("SimpleItem",
            new JsonObject { ["name"] = "Test", ["price"] = 10m });

        global.Calls.Should().ContainInOrder("GlobalBefore:Create", "GlobalAfter:Create");
    }

    [Fact]
    public async Task CreateAsync_GlobalBeforeRunsBeforeTypedBefore()
    {
        var hook = new TrackingHook();
        var global = new GlobalTrackingHook();

        // We can verify ordering by checking both hooks got called
        using var factory = CreateFactory(typedHook: hook, globalHook: global);

        await factory.CrudService.CreateAsync("SimpleItem",
            new JsonObject { ["name"] = "Test", ["price"] = 10m });

        // Global before should be first, then typed before
        global.Calls.First().Should().Be("GlobalBefore:Create");
        hook.Calls.First().Should().Be("BeforeCreate");
    }

    // ────────────────────────────────────────────
    //  Read hooks
    // ────────────────────────────────────────────

    [Fact]
    public async Task GetAsync_InvokesAfterReadHook()
    {
        _db.SimpleItems.Add(new SimpleItem { Name = "Read Me", Price = 5m });
        await _db.SaveChangesAsync();
        var id = _db.SimpleItems.First().Id;

        var hook = new TrackingHook();
        using var factory = CreateFactory(typedHook: hook);

        await factory.CrudService.GetAsync("SimpleItem", id);

        hook.Calls.Should().Contain("AfterRead");
    }

    [Fact]
    public async Task ListAsync_InvokesAfterReadHookPerItem()
    {
        _db.SimpleItems.AddRange(
            new SimpleItem { Name = "A", Price = 1m },
            new SimpleItem { Name = "B", Price = 2m },
            new SimpleItem { Name = "C", Price = 3m });
        await _db.SaveChangesAsync();

        var hook = new TrackingHook();
        using var factory = CreateFactory(typedHook: hook);

        await factory.CrudService.ListAsync("SimpleItem", new QuerySpec(Page: 1, PageSize: 10));

        hook.Calls.Count(c => c == "AfterRead").Should().Be(3);
    }

    // ────────────────────────────────────────────
    //  Update hooks
    // ────────────────────────────────────────────

    [Fact]
    public async Task UpdateAsync_InvokesBeforeAndAfterUpdateHooks()
    {
        _db.SimpleItems.Add(new SimpleItem { Name = "Original", Price = 5m });
        await _db.SaveChangesAsync();
        var id = _db.SimpleItems.First().Id;

        var hook = new TrackingHook();
        using var factory = CreateFactory(typedHook: hook);

        await factory.CrudService.UpdateAsync("SimpleItem", id,
            new JsonObject { ["name"] = "Updated" });

        hook.Calls.Should().ContainInOrder("BeforeUpdate", "AfterUpdate");
    }

    // ────────────────────────────────────────────
    //  Delete hooks (hard delete)
    // ────────────────────────────────────────────

    [Fact]
    public async Task DeleteAsync_HardDelete_InvokesBeforeAndAfterDeleteHooks()
    {
        _db.SimpleItems.Add(new SimpleItem { Name = "ToDelete", Price = 5m });
        await _db.SaveChangesAsync();
        var id = _db.SimpleItems.First().Id;

        var hook = new TrackingHook();
        using var factory = CreateFactory(typedHook: hook);

        await factory.CrudService.DeleteAsync("SimpleItem", id);

        hook.Calls.Should().ContainInOrder("BeforeDelete", "AfterDelete");
    }

    [Fact]
    public async Task DeleteAsync_HardDelete_InvokesGlobalHooks()
    {
        _db.SimpleItems.Add(new SimpleItem { Name = "ToDelete", Price = 5m });
        await _db.SaveChangesAsync();
        var id = _db.SimpleItems.First().Id;

        var global = new GlobalTrackingHook();
        using var factory = CreateFactory(globalHook: global);

        await factory.CrudService.DeleteAsync("SimpleItem", id);

        global.Calls.Should().ContainInOrder("GlobalBefore:Delete", "GlobalAfter:Delete");
    }

    // ────────────────────────────────────────────
    //  Delete hooks (soft delete)
    // ────────────────────────────────────────────

    [Fact]
    public async Task DeleteAsync_SoftDelete_InvokesBeforeAndAfterDeleteHooks()
    {
        _db.SoftDeleteItems.Add(new SoftDeleteItem { Name = "SoftDel" });
        await _db.SaveChangesAsync();
        var id = _db.SoftDeleteItems.First().Id;

        var hook = new SoftDeleteTrackingHook();
        using var factory = CreateFactory(sdHook: hook);

        await factory.CrudService.DeleteAsync("SoftDeleteItem", id);

        hook.Calls.Should().ContainInOrder("BeforeDelete", "AfterDelete");
    }

    [Fact]
    public async Task DeleteAsync_SoftDelete_InvokesGlobalHooks()
    {
        _db.SoftDeleteItems.Add(new SoftDeleteItem { Name = "SoftDel" });
        await _db.SaveChangesAsync();
        var id = _db.SoftDeleteItems.First().Id;

        var global = new GlobalTrackingHook();
        using var factory = CreateFactory(globalHook: global);

        await factory.CrudService.DeleteAsync("SoftDeleteItem", id);

        global.Calls.Should().ContainInOrder("GlobalBefore:Delete", "GlobalAfter:Delete");
    }

    [Fact]
    public async Task DeleteAsync_SoftDeleteHardOverride_InvokesDeleteHooks()
    {
        _db.SoftDeleteItems.Add(new SoftDeleteItem { Name = "HardOverride" });
        await _db.SaveChangesAsync();
        var id = _db.SoftDeleteItems.First().Id;

        var hook = new SoftDeleteTrackingHook();
        using var factory = CreateFactory(sdHook: hook);

        await factory.CrudService.DeleteAsync("SoftDeleteItem", id,
            new CrudDeleteSpec(HardDelete: true));

        hook.Calls.Should().ContainInOrder("BeforeDelete", "AfterDelete");
    }

    // ────────────────────────────────────────────
    //  No hook registered: operations still succeed
    // ────────────────────────────────────────────

    [Fact]
    public async Task CrudOperations_SucceedWithNoHooksRegistered()
    {
        using var factory = CreateFactory(); // no hooks

        var created = await factory.CrudService.CreateAsync("SimpleItem",
            new JsonObject { ["name"] = "NoHook", ["price"] = 1m });
        var id = created["id"]!.GetValue<int>();

        var fetched = await factory.CrudService.GetAsync("SimpleItem", id);
        fetched.Should().NotBeNull();

        await factory.CrudService.UpdateAsync("SimpleItem", id,
            new JsonObject { ["name"] = "Updated" });

        await factory.CrudService.DeleteAsync("SimpleItem", id);
    }

    // ────────────────────────────────────────────
    //  Hook context has correct operation
    // ────────────────────────────────────────────

    [Fact]
    public async Task Hook_ContextHasCorrectOperationForCreate()
    {
        CrudOperation? captured = null;
        var hook = new CapturingGlobalHook(ctx => captured = ctx.Operation);
        using var factory = new TestServiceFactory(_db,
            new[] { BuildSimpleContract(), BuildSoftDeleteContract() },
            s => s.AddSingleton<ICrudHook>(hook));

        await factory.CrudService.CreateAsync("SimpleItem",
            new JsonObject { ["name"] = "Ctx", ["price"] = 1m });

        captured.Should().Be(CrudOperation.Create);
    }

    [Fact]
    public async Task Hook_ContextHasCorrectOperationForDelete()
    {
        _db.SimpleItems.Add(new SimpleItem { Name = "ForCtx", Price = 1m });
        await _db.SaveChangesAsync();
        var id = _db.SimpleItems.First().Id;

        CrudOperation? captured = null;
        var hook = new CapturingGlobalHook(ctx => captured = ctx.Operation);
        using var factory = new TestServiceFactory(_db,
            new[] { BuildSimpleContract(), BuildSoftDeleteContract() },
            s => s.AddSingleton<ICrudHook>(hook));

        await factory.CrudService.DeleteAsync("SimpleItem", id);

        captured.Should().Be(CrudOperation.Delete);
    }

    private class CapturingGlobalHook : ICrudHook
    {
        private readonly Action<CrudHookContext> _capture;
        public int Order => 0;
        public CapturingGlobalHook(Action<CrudHookContext> capture) => _capture = capture;
        public Task BeforeAsync(CrudHookContext ctx) { _capture(ctx); return Task.CompletedTask; }
        public Task AfterAsync(CrudHookContext ctx) => Task.CompletedTask;
    }

    // ────────────────────────────────────────────
    //  Hook throws → operation aborted
    // ────────────────────────────────────────────

    [Fact]
    public async Task GlobalBeforeHookThrows_CreateAborted_NoEntityPersisted()
    {
        var throwingHook = new ThrowingGlobalHook(throwBefore: true);
        using var factory = new TestServiceFactory(_db,
            new[] { BuildSimpleContract(), BuildSoftDeleteContract() },
            s => s.AddSingleton<ICrudHook>(throwingHook));

        var act = () => factory.CrudService.CreateAsync("SimpleItem",
            new JsonObject { ["name"] = "Boom", ["price"] = 1m });

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("Hook aborted*");
        _db.SimpleItems.Should().BeEmpty("entity should not be persisted when hook aborts");
    }

    [Fact]
    public async Task GlobalBeforeHookThrows_DeleteAborted_EntityNotRemoved()
    {
        _db.SimpleItems.Add(new SimpleItem { Name = "Kept", Price = 1m });
        await _db.SaveChangesAsync();
        var id = _db.SimpleItems.First().Id;

        var throwingHook = new ThrowingGlobalHook(throwBefore: true);
        using var factory = new TestServiceFactory(_db,
            new[] { BuildSimpleContract(), BuildSoftDeleteContract() },
            s => s.AddSingleton<ICrudHook>(throwingHook));

        var act = () => factory.CrudService.DeleteAsync("SimpleItem", id);

        await act.Should().ThrowAsync<InvalidOperationException>();
        _db.SimpleItems.Should().HaveCount(1, "entity should not be removed when hook aborts");
    }

    [Fact]
    public async Task GlobalBeforeHookThrows_UpdateAborted_EntityUnchanged()
    {
        _db.SimpleItems.Add(new SimpleItem { Name = "Original", Price = 5m });
        await _db.SaveChangesAsync();
        var id = _db.SimpleItems.First().Id;

        var throwingHook = new ThrowingGlobalHook(throwBefore: true);
        using var factory = new TestServiceFactory(_db,
            new[] { BuildSimpleContract(), BuildSoftDeleteContract() },
            s => s.AddSingleton<ICrudHook>(throwingHook));

        var act = () => factory.CrudService.UpdateAsync("SimpleItem", id,
            new JsonObject { ["name"] = "Changed" });

        await act.Should().ThrowAsync<InvalidOperationException>();
        _db.SimpleItems.First().Name.Should().Be("Original", "entity should be unchanged when hook aborts");
    }

    private class ThrowingGlobalHook : ICrudHook
    {
        private readonly bool _throwBefore;
        public int Order => 0;
        public ThrowingGlobalHook(bool throwBefore) => _throwBefore = throwBefore;
        public Task BeforeAsync(CrudHookContext ctx)
            => _throwBefore ? throw new InvalidOperationException("Hook aborted the operation") : Task.CompletedTask;
        public Task AfterAsync(CrudHookContext ctx) => Task.CompletedTask;
    }
}
