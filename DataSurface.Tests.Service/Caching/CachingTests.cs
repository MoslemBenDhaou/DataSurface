using System.Text.Json.Nodes;
using DataSurface.Core.Contracts;
using DataSurface.Core.Enums;
using DataSurface.EFCore.Caching;
using DataSurface.EFCore.Contracts;
using DataSurface.EFCore.Interfaces;
using DataSurface.EFCore.Services;
using DataSurface.Tests.Service.Shared;
using DataSurface.Tests.Service.Shared.Builders;
using DataSurface.Tests.Service.Shared.Entities;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace DataSurface.Tests.Service.Caching;

/// <summary>
/// Tests for query result caching: cache hit/miss, invalidation on writes,
/// different query params produce different keys, and security bypass.
/// Covers strategy §4.8.
/// </summary>
public class CachingTests : IDisposable
{
    private readonly CrudTestDbContext _db;

    public CachingTests()
    {
        var options = new DbContextOptionsBuilder<CrudTestDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _db = new CrudTestDbContext(options);
        _db.Database.EnsureCreated();
    }

    public void Dispose() => _db.Dispose();

    private static ResourceContract BuildContract()
    {
        return new ResourceContractBuilder("SimpleItem", "simple-items")
            .Key("Id", FieldType.Int32)
            .WithField(new FieldBuilder("Id").OfType(FieldType.Int32).InRead().Filterable().Sortable().Build())
            .WithField(new FieldBuilder("Name").OfType(FieldType.String).ReadCreateUpdate().RequiredOnCreate().Searchable().Build())
            .WithField(new FieldBuilder("Price").OfType(FieldType.Decimal).ReadCreateUpdate().Build())
            .EnableAllOperations()
            .Build();
    }

    private static ResourceContract BuildTenantContract()
    {
        return new ResourceContractBuilder("TenantItem", "tenant-items")
            .Key("Id", FieldType.Int32)
            .Tenant("TenantId", "tenantId", "tenant_id", required: true)
            .WithField(new FieldBuilder("Id").OfType(FieldType.Int32).InRead().Build())
            .WithField(new FieldBuilder("Name").OfType(FieldType.String).ReadCreateUpdate().RequiredOnCreate().Build())
            .WithField(new FieldBuilder("TenantId").OfType(FieldType.String).InRead().Build())
            .WithField(new FieldBuilder("Price").OfType(FieldType.Decimal).ReadCreateUpdate().Build())
            .EnableAllOperations()
            .Build();
    }

    private static IQueryResultCache CreateCache()
    {
        var memCache = new MemoryDistributedCache(Options.Create(new MemoryDistributedCacheOptions()));
        var opts = Options.Create(new DataSurfaceCacheOptions
        {
            EnableQueryCaching = true,
            DefaultCacheDuration = TimeSpan.FromMinutes(5),
            CacheKeyPrefix = "test:"
        });
        return new DistributedQueryResultCache(memCache, opts);
    }

    // ────────────────────────────────────────────
    //  Cache Miss then Cache Hit
    // ────────────────────────────────────────────

    [Fact]
    public async Task List_FirstCall_CacheMiss_SecondCall_CacheHit()
    {
        _db.SimpleItems.AddRange(
            new SimpleItem { Name = "A", Price = 1m },
            new SimpleItem { Name = "B", Price = 2m });
        await _db.SaveChangesAsync();

        var cache = CreateCache();

        using var factory = new TestServiceFactory(_db, new[] { BuildContract() }, svc =>
        {
            svc.AddSingleton(cache);
        });

        // First call — cache miss, goes to DB
        var result1 = await factory.CrudService.ListAsync("SimpleItem", new QuerySpec());
        result1.Items.Should().HaveCount(2);

        // Add another item directly to DB (cache shouldn't know)
        _db.SimpleItems.Add(new SimpleItem { Name = "C", Price = 3m });
        await _db.SaveChangesAsync();

        // Second call — cache hit, returns stale 2 items
        var result2 = await factory.CrudService.ListAsync("SimpleItem", new QuerySpec());
        result2.Items.Should().HaveCount(2, "second call should return cached result");
    }

    [Fact]
    public async Task Get_FirstCall_CacheMiss_SecondCall_CacheHit()
    {
        _db.SimpleItems.Add(new SimpleItem { Name = "Cached", Price = 10m });
        await _db.SaveChangesAsync();
        var id = _db.SimpleItems.First().Id;

        var cache = CreateCache();

        using var factory = new TestServiceFactory(_db, new[] { BuildContract() }, svc =>
        {
            svc.AddSingleton(cache);
        });

        var result1 = await factory.CrudService.GetAsync("SimpleItem", id);
        result1.Should().NotBeNull();

        // Mutate DB directly
        _db.SimpleItems.First().Name = "Mutated";
        await _db.SaveChangesAsync();

        // Second call returns cached (old name)
        var result2 = await factory.CrudService.GetAsync("SimpleItem", id);
        result2!["name"]!.GetValue<string>().Should().Be("Cached");
    }

    // ────────────────────────────────────────────
    //  Create Invalidates List Cache
    // ────────────────────────────────────────────

    [Fact]
    public async Task Create_InvalidatesListCache()
    {
        _db.SimpleItems.Add(new SimpleItem { Name = "Initial", Price = 1m });
        await _db.SaveChangesAsync();

        var cache = CreateCache();

        using var factory = new TestServiceFactory(_db, new[] { BuildContract() }, svc =>
        {
            svc.AddSingleton(cache);
        });

        // Warm the cache
        var result1 = await factory.CrudService.ListAsync("SimpleItem", new QuerySpec());
        result1.Items.Should().HaveCount(1);

        // Create new item (should invalidate list cache)
        await factory.CrudService.CreateAsync("SimpleItem",
            new JsonObject { ["name"] = "New", ["price"] = 2m });

        // List should now go to DB and return fresh data
        var result2 = await factory.CrudService.ListAsync("SimpleItem", new QuerySpec());
        result2.Items.Should().HaveCount(2);
    }

    // ────────────────────────────────────────────
    //  Update Invalidates Cache
    // ────────────────────────────────────────────

    [Fact]
    public async Task Update_InvalidatesEntityAndListCache()
    {
        _db.SimpleItems.Add(new SimpleItem { Name = "Original", Price = 1m });
        await _db.SaveChangesAsync();
        var id = _db.SimpleItems.First().Id;

        var cache = CreateCache();

        using var factory = new TestServiceFactory(_db, new[] { BuildContract() }, svc =>
        {
            svc.AddSingleton(cache);
        });

        // Warm caches
        await factory.CrudService.GetAsync("SimpleItem", id);
        await factory.CrudService.ListAsync("SimpleItem", new QuerySpec());

        // Update (should invalidate both caches)
        await factory.CrudService.UpdateAsync("SimpleItem", id,
            new JsonObject { ["name"] = "Updated" });

        // Get should return fresh data
        var fresh = await factory.CrudService.GetAsync("SimpleItem", id);
        fresh!["name"]!.GetValue<string>().Should().Be("Updated");
    }

    // ────────────────────────────────────────────
    //  Delete Invalidates Cache
    // ────────────────────────────────────────────

    [Fact]
    public async Task Delete_InvalidatesEntityAndListCache()
    {
        _db.SimpleItems.AddRange(
            new SimpleItem { Name = "Keep", Price = 1m },
            new SimpleItem { Name = "Remove", Price = 2m });
        await _db.SaveChangesAsync();
        var removeId = _db.SimpleItems.First(x => x.Name == "Remove").Id;

        var cache = CreateCache();

        using var factory = new TestServiceFactory(_db, new[] { BuildContract() }, svc =>
        {
            svc.AddSingleton(cache);
        });

        // Warm cache
        await factory.CrudService.ListAsync("SimpleItem", new QuerySpec());

        // Delete
        await factory.CrudService.DeleteAsync("SimpleItem", removeId);

        // List should be fresh
        var result = await factory.CrudService.ListAsync("SimpleItem", new QuerySpec());
        result.Items.Should().HaveCount(1);
    }

    // ────────────────────────────────────────────
    //  Different Query Params = Different Cache Key
    // ────────────────────────────────────────────

    [Fact]
    public async Task DifferentQueryParams_ProduceDifferentCacheKeys()
    {
        var cache = CreateCache();
        var key1 = cache.GenerateListCacheKey("SimpleItem", new QuerySpec { Page = 1, PageSize = 10 }, null);
        var key2 = cache.GenerateListCacheKey("SimpleItem", new QuerySpec { Page = 2, PageSize = 10 }, null);
        var key3 = cache.GenerateListCacheKey("SimpleItem", new QuerySpec { Page = 1, PageSize = 10, Sort = "-name" }, null);

        key1.Should().NotBe(key2);
        key1.Should().NotBe(key3);
        key2.Should().NotBe(key3);
    }

    // ────────────────────────────────────────────
    //  Cache Disabled → Always Fetches from DB
    // ────────────────────────────────────────────

    [Fact]
    public async Task CacheDisabled_AlwaysFetchesFromDB()
    {
        _db.SimpleItems.Add(new SimpleItem { Name = "A", Price = 1m });
        await _db.SaveChangesAsync();

        // Create a disabled cache
        var memCache = new MemoryDistributedCache(Options.Create(new MemoryDistributedCacheOptions()));
        var disabledOpts = Options.Create(new DataSurfaceCacheOptions
        {
            EnableQueryCaching = false
        });
        var disabledCache = new DistributedQueryResultCache(memCache, disabledOpts);

        using var factory = new TestServiceFactory(_db, new[] { BuildContract() }, svc =>
        {
            svc.AddSingleton<IQueryResultCache>(disabledCache);
        });

        var r1 = await factory.CrudService.ListAsync("SimpleItem", new QuerySpec());
        r1.Items.Should().HaveCount(1);

        _db.SimpleItems.Add(new SimpleItem { Name = "B", Price = 2m });
        await _db.SaveChangesAsync();

        // Should go directly to DB since caching is disabled
        var r2 = await factory.CrudService.ListAsync("SimpleItem", new QuerySpec());
        r2.Items.Should().HaveCount(2);
    }

    // ────────────────────────────────────────────
    //  Security Active → Cache Bypassed
    // ────────────────────────────────────────────

    [Fact]
    public async Task TenantIsolationActive_CacheBypassed()
    {
        _db.TenantItems.AddRange(
            new TenantItem { Name = "T1", TenantId = "t1", Price = 1m },
            new TenantItem { Name = "T2", TenantId = "t2", Price = 2m });
        await _db.SaveChangesAsync();

        var cache = CreateCache();

        using var factory = new TestServiceFactory(_db, new[] { BuildTenantContract() }, svc =>
        {
            svc.AddSingleton<ITenantResolver>(new FixedTenantResolver("t1"));
            svc.AddSingleton(cache);
            svc.AddSingleton<CrudSecurityDispatcher>();
        });

        // First call with tenant isolation
        var r1 = await factory.CrudService.ListAsync("TenantItem", new QuerySpec());
        r1.Items.Should().HaveCount(1);

        // Add another item for same tenant directly
        _db.TenantItems.Add(new TenantItem { Name = "T1-new", TenantId = "t1", Price = 3m });
        await _db.SaveChangesAsync();

        // Should NOT use cache because tenant isolation is active
        var r2 = await factory.CrudService.ListAsync("TenantItem", new QuerySpec());
        r2.Items.Should().HaveCount(2, "cache should be bypassed when tenant isolation is active");
    }

    private sealed class FixedTenantResolver : ITenantResolver
    {
        private readonly string? _tenantId;
        public FixedTenantResolver(string? tenantId) => _tenantId = tenantId;
        public string? GetTenantId() => _tenantId;
    }
}
