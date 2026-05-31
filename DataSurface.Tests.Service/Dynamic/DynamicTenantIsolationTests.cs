using System.Text.Json.Nodes;
using DataSurface.Core.Enums;
using DataSurface.Dynamic.DI;
using DataSurface.Dynamic.Entities;
using DataSurface.Dynamic.Model;
using DataSurface.Dynamic.Services;
using DataSurface.EFCore.Contracts;
using DataSurface.EFCore.Interfaces;
using DataSurface.EFCore.Services;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DataSurface.Tests.Service.Dynamic;

/// <summary>
/// Regression test for B6: the dynamic (JSON-backed) CRUD service must enforce tenant
/// isolation — records are stamped with the current tenant on create and filtered by it on
/// read, so one tenant cannot see or fetch another tenant's records.
/// </summary>
public class DynamicTenantIsolationTests
{
    private sealed class DynDbContext : DbContext
    {
        public DynDbContext(DbContextOptions<DynDbContext> o) : base(o) { }
        protected override void OnModelCreating(ModelBuilder mb)
        {
            mb.AddDataSurfaceDynamic();
            // The InMemory provider does not generate store row-versions (a real relational
            // provider auto-populates it), so relax the requirement for the test provider only.
            mb.Entity<DsDynamicRecordRow>().Property(x => x.RowVersion).IsRequired(false).ValueGeneratedNever();
        }
    }

    private sealed class MutableTenant : ITenantResolver
    {
        public string? TenantId { get; set; }
        public string? GetTenantId() => TenantId;
    }

    [Fact]
    public async Task DynamicCrud_EnforcesTenantIsolation()
    {
        var tenant = new MutableTenant();

        var dbName = Guid.NewGuid().ToString();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<DynDbContext>(o => o.UseInMemoryDatabase(dbName));
        services.AddScoped<DbContext>(sp => sp.GetRequiredService<DynDbContext>());
        services.AddSingleton<ITenantResolver>(tenant);
        services.AddDataSurfaceEfCore(_ => { });
        services.AddDataSurfaceDynamic();

        using var provider = services.BuildServiceProvider();

        // Seed a tenant-scoped dynamic entity definition.
        using (var scope = provider.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<DynDbContext>();
            db.Database.EnsureCreated();
            db.Set<DsEntityDefRow>().Add(new DsEntityDefRow
            {
                EntityKey = "Widget",
                Route = "widgets",
                Backend = StorageBackend.DynamicJson,
                KeyName = "id",
                KeyType = FieldType.Guid,
                TenantFieldName = "tenant",
                TenantFieldApiName = "tenant",
                TenantClaimType = "tenant_id",
                TenantRequired = true,
                Properties = new List<DsPropertyDefRow>
                {
                    new() { Name = "name", ApiName = "name", Type = FieldType.String, InFlags = CrudDto.Read | CrudDto.Create | CrudDto.Update, RequiredOnCreate = true },
                    new() { Name = "tenant", ApiName = "tenant", Type = FieldType.String, InFlags = CrudDto.Read }
                }
            });
            await db.SaveChangesAsync();
        }

        async Task<JsonObject> CreateFor(string t, string name)
        {
            tenant.TenantId = t;
            using var scope = provider.CreateScope();
            var crud = scope.ServiceProvider.GetRequiredService<IDataSurfaceCrudService>();
            return await crud.CreateAsync("Widget", new JsonObject { ["name"] = name });
        }

        await CreateFor("a", "Alpha");
        var wb = await CreateFor("b", "Beta");

        // As tenant "a": list returns only Alpha, and tenant B's record is invisible.
        tenant.TenantId = "a";
        using (var scope = provider.CreateScope())
        {
            var crud = scope.ServiceProvider.GetRequiredService<IDataSurfaceCrudService>();

            var list = await crud.ListAsync("Widget", new QuerySpec());
            list.Items.Should().HaveCount(1);
            list.Items[0]["name"]!.GetValue<string>().Should().Be("Alpha");

            var bId = wb["id"]!.GetValue<string>();
            (await crud.GetAsync("Widget", bId)).Should().BeNull("tenant A must not see tenant B's record");
        }
    }
}
