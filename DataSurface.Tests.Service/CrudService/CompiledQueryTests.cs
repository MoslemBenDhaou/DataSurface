using System.Linq.Expressions;
using DataSurface.Core.Contracts;
using DataSurface.Core.Enums;
using DataSurface.EFCore.Contracts;
using DataSurface.EFCore.Interfaces;
using DataSurface.EFCore.Queries;
using DataSurface.EFCore.Services;
using DataSurface.Tests.Service.Shared;
using DataSurface.Tests.Service.Shared.Builders;
using DataSurface.Tests.Service.Shared.Entities;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DataSurface.Tests.Service.CrudService;

/// <summary>
/// P4: the compiled-query fast path for simple by-id reads. When a resource needs none of the
/// per-request dynamic query composition (no tenant, soft-delete, row-level security, or expand),
/// <c>GetAsync</c> serves the primary-key lookup from a cached compiled async query — without
/// regressing correctness. When a <see cref="CompiledQueryCache"/> is not registered, the dynamic
/// path is used (covered by the rest of the suite).
/// </summary>
public class CompiledQueryTests : IDisposable
{
    private readonly CrudTestDbContext _db;

    public CompiledQueryTests()
    {
        var options = new DbContextOptionsBuilder<CrudTestDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _db = new CrudTestDbContext(options);
        _db.Database.EnsureCreated();
    }

    public void Dispose() => _db.Dispose();

    // A plain resource: no [CrudTenant], not ISoftDelete, no security registered, no default expand.
    private static ResourceContract Contract()
        => new ResourceContractBuilder("SimpleItem", "simple-items")
            .Key("Id", FieldType.Int32)
            .WithField(new FieldBuilder("Id").OfType(FieldType.Int32).InRead().Build())
            .WithField(new FieldBuilder("Name").OfType(FieldType.String).ReadCreateUpdate().RequiredOnCreate().Build())
            .EnableAllOperations()
            .Build();

    [Fact]
    public async Task Get_SimpleResource_UsesCompiledQuery_AndReturnsEntity()
    {
        _db.SimpleItems.Add(new SimpleItem { Name = "Alpha", Price = 1m });
        await _db.SaveChangesAsync();
        var seeded = await _db.SimpleItems.AsNoTracking().FirstAsync();

        var cache = new CompiledQueryCache();
        using var factory = new TestServiceFactory(_db, new[] { Contract() }, svc => svc.AddSingleton(cache));

        var result = await factory.CrudService.GetAsync("SimpleItem", seeded.Id);

        result.Should().NotBeNull();
        result!["id"]!.GetValue<int>().Should().Be(seeded.Id);
        result["name"]!.GetValue<string>().Should().Be("Alpha");
        cache.GetStats().FindByIdQueryCount.Should().BeGreaterThan(0,
            "the compiled by-id fast path should have been used for this simple resource");
    }

    [Fact]
    public async Task Get_Missing_OnFastPath_ReturnsNull()
    {
        var cache = new CompiledQueryCache();
        using var factory = new TestServiceFactory(_db, new[] { Contract() }, svc => svc.AddSingleton(cache));

        var result = await factory.CrudService.GetAsync("SimpleItem", 999);

        result.Should().BeNull();
    }

    private sealed class ExcludeIdFilter(int excludedId) : IResourceFilter<SimpleItem>
    {
        public Expression<Func<SimpleItem, bool>>? GetFilter(ResourceContract contract) => e => e.Id != excludedId;
    }

    [Fact]
    public async Task Get_WithTypedResourceFilter_FastPathDoesNotBypassRowLevelSecurity()
    {
        _db.SimpleItems.Add(new SimpleItem { Name = "Hidden", Price = 1m });
        _db.SimpleItems.Add(new SimpleItem { Name = "Visible", Price = 2m });
        await _db.SaveChangesAsync();
        var hidden = await _db.SimpleItems.AsNoTracking().FirstAsync(x => x.Name == "Hidden");

        var cache = new CompiledQueryCache();
        using var factory = new TestServiceFactory(_db, new[] { Contract() }, svc =>
        {
            svc.AddSingleton(cache);
            svc.AddScoped<CrudSecurityDispatcher>();                                  // activates _security
            svc.AddSingleton<IResourceFilter<SimpleItem>>(new ExcludeIdFilter(hidden.Id));
        });

        // The typed IResourceFilter<SimpleItem> excludes 'hidden'; the compiled by-id fast path must NOT bypass it.
        var result = await factory.CrudService.GetAsync("SimpleItem", hidden.Id);

        result.Should().BeNull("HasPerUserSecurity must detect IResourceFilter<SimpleItem> and take the full filtered path");
    }
}
