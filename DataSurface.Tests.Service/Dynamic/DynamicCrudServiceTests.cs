using System.Text.Json.Nodes;
using DataSurface.Core.Enums;
using DataSurface.Dynamic.DI;
using DataSurface.Dynamic.Entities;
using DataSurface.Dynamic.Model;
using DataSurface.EFCore.Contracts;
using DataSurface.EFCore.Exceptions;
using DataSurface.EFCore.Interfaces;
using DataSurface.EFCore.Services;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DataSurface.Tests.Service.Dynamic;

/// <summary>
/// Behavioral regression tests for the dynamic (JSON-backed) CRUD service:
/// Guid id canonicalization, canonical-casing storage, id collision handling,
/// filter operator parsing and delete semantics.
/// </summary>
public class DynamicCrudServiceTests
{
    private sealed class DynDbContext : DbContext
    {
        public DynDbContext(DbContextOptions<DynDbContext> o) : base(o) { }
        protected override void OnModelCreating(ModelBuilder mb)
        {
            mb.AddDataSurfaceDynamic();
            // The InMemory provider does not generate store row-versions; relax for tests.
            mb.Entity<DsDynamicRecordRow>().Property(x => x.RowVersion).IsRequired(false).ValueGeneratedNever();
        }
    }

    private static ServiceProvider BuildProvider()
    {
        var dbName = Guid.NewGuid().ToString();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<DynDbContext>(o => o.UseInMemoryDatabase(dbName));
        services.AddScoped<DbContext>(sp => sp.GetRequiredService<DynDbContext>());
        services.AddDataSurfaceEfCore(_ => { });
        services.AddDataSurfaceDynamic();
        return services.BuildServiceProvider();
    }

    private static async Task SeedWidgetAsync(
        ServiceProvider provider,
        bool keyClientSettable = false,
        bool enableDelete = true)
    {
        using var scope = provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<DynDbContext>();
        db.Database.EnsureCreated();

        var props = new List<DsPropertyDefRow>
        {
            new()
            {
                Name = "name", ApiName = "name", Type = FieldType.String,
                InFlags = CrudDto.Read | CrudDto.Create | CrudDto.Update | CrudDto.Filter | CrudDto.Sort
            },
            new()
            {
                Name = "code", ApiName = "code", Type = FieldType.String,
                InFlags = CrudDto.Read | CrudDto.Create | CrudDto.Update | CrudDto.Filter
            }
        };

        if (keyClientSettable)
        {
            props.Add(new DsPropertyDefRow
            {
                Name = "id", ApiName = "id", Type = FieldType.Guid,
                InFlags = CrudDto.Read | CrudDto.Create | CrudDto.Filter
            });
        }

        db.Set<DsEntityDefRow>().Add(new DsEntityDefRow
        {
            EntityKey = "Widget",
            Route = "widgets",
            Backend = StorageBackend.DynamicJson,
            KeyName = "id",
            KeyType = FieldType.Guid,
            EnableDelete = enableDelete,
            Properties = props
        });
        await db.SaveChangesAsync();
    }

    private static IDataSurfaceCrudService Crud(IServiceScope scope)
        => scope.ServiceProvider.GetRequiredService<IDataSurfaceCrudService>();

    // ---- Finding 1/6: Guid id canonical format round-trip ----

    [Fact]
    public async Task GuidId_GeneratedInDFormat_AndRoundTripsThroughAnyGuidRepresentation()
    {
        using var provider = BuildProvider();
        await SeedWidgetAsync(provider);

        string id;
        using (var scope = provider.CreateScope())
        {
            var created = await Crud(scope).CreateAsync("Widget", new JsonObject { ["name"] = "A" });
            id = created["id"]!.GetValue<string>();
        }

        // Generated id must be in the canonical "D" (hyphenated) format.
        id.Should().Contain("-", "Guid ids are canonicalized to the 'D' format");
        Guid.TryParse(id, out var guid).Should().BeTrue();
        id.Should().Be(guid.ToString("D"));

        // Get by Guid object (how the HTTP layer passes route ids after Guid.Parse).
        using (var scope = provider.CreateScope())
        {
            var byGuid = await Crud(scope).GetAsync("Widget", guid);
            byGuid.Should().NotBeNull("the route id parsed as a Guid must resolve the record");
            byGuid!["name"]!.GetValue<string>().Should().Be("A");
        }

        // Get by "N" (dashless) string — any Guid representation must normalize to "D".
        using (var scope = provider.CreateScope())
        {
            var byN = await Crud(scope).GetAsync("Widget", guid.ToString("N"));
            byN.Should().NotBeNull();
            byN!["name"]!.GetValue<string>().Should().Be("A");
        }
    }

    [Fact]
    public async Task GuidId_ClientSupplied_IsNormalizedToDFormat()
    {
        using var provider = BuildProvider();
        await SeedWidgetAsync(provider, keyClientSettable: true);

        var guid = Guid.NewGuid();
        using var scope = provider.CreateScope();
        var created = await Crud(scope).CreateAsync("Widget", new JsonObject
        {
            ["id"] = guid.ToString("N"), // dashless on purpose
            ["name"] = "A"
        });

        created["id"]!.GetValue<string>().Should().Be(guid.ToString("D"));

        var fetched = await Crud(scope).GetAsync("Widget", guid);
        fetched.Should().NotBeNull();
    }

    // ---- Finding 3: canonical-casing storage ----

    [Fact]
    public async Task Create_WithDifferentlyCasedFieldName_StoresUnderCanonicalApiName()
    {
        using var provider = BuildProvider();
        await SeedWidgetAsync(provider);

        using var scope = provider.CreateScope();
        var created = await Crud(scope).CreateAsync("Widget", new JsonObject { ["NAME"] = "Café" });

        // The read shape projects the canonical apiName, so the value must be found there.
        created["name"]!.GetValue<string>().Should().Be("Café", "values must be stored under the canonical apiName without JSON escaping");

        var id = created["id"]!.GetValue<string>();
        var fetched = await Crud(scope).GetAsync("Widget", id);
        fetched!["name"]!.GetValue<string>().Should().Be("Café");

        // Indexing must find canonical values: filter by name matches.
        var list = await Crud(scope).ListAsync("Widget", new QuerySpec(Filters: new Dictionary<string, string> { ["name"] = "Café" }));
        list.Items.Should().HaveCount(1);
    }

    // ---- Finding 8: client-supplied id collision -> 400 ----

    [Fact]
    public async Task Create_WithExistingId_ThrowsValidation400_InsteadOfDbUpdateException()
    {
        using var provider = BuildProvider();
        await SeedWidgetAsync(provider, keyClientSettable: true);

        var id = Guid.NewGuid().ToString("D");

        using var scope = provider.CreateScope();
        await Crud(scope).CreateAsync("Widget", new JsonObject { ["id"] = id, ["name"] = "First" });

        var act = () => Crud(scope).CreateAsync("Widget", new JsonObject { ["id"] = id, ["name"] = "Second" });
        var ex = await act.Should().ThrowAsync<CrudRequestValidationException>();
        ex.Which.Errors.Values.SelectMany(v => v).Should().Contain(m => m.Contains("already exists"));
    }

    [Fact]
    public async Task Create_WithSoftDeletedId_StillCollides()
    {
        using var provider = BuildProvider();
        await SeedWidgetAsync(provider, keyClientSettable: true);

        var id = Guid.NewGuid().ToString("D");

        using var scope = provider.CreateScope();
        await Crud(scope).CreateAsync("Widget", new JsonObject { ["id"] = id, ["name"] = "First" });
        await Crud(scope).DeleteAsync("Widget", id); // soft delete

        var act = () => Crud(scope).CreateAsync("Widget", new JsonObject { ["id"] = id, ["name"] = "Second" });
        await act.Should().ThrowAsync<CrudRequestValidationException>(
            "the (EntityKey, Id) primary key still exists for soft-deleted rows");
    }

    // ---- Finding 5: ParseOp must not mangle values containing ':' ----

    [Fact]
    public async Task Filter_ValueContainingColon_IsTreatedAsEqValue()
    {
        using var provider = BuildProvider();
        await SeedWidgetAsync(provider);

        using var scope = provider.CreateScope();
        await Crud(scope).CreateAsync("Widget", new JsonObject { ["name"] = "A", ["code"] = "urn:x:1" });
        await Crud(scope).CreateAsync("Widget", new JsonObject { ["name"] = "B", ["code"] = "other" });

        // "urn" is not a known operator: the whole raw value must be used as an eq match.
        var list = await Crud(scope).ListAsync("Widget", new QuerySpec(Filters: new Dictionary<string, string> { ["code"] = "urn:x:1" }));
        list.Items.Should().HaveCount(1);
        list.Items[0]["name"]!.GetValue<string>().Should().Be("A");

        // An explicit known operator still works, even when the value itself contains colons.
        var list2 = await Crud(scope).ListAsync("Widget", new QuerySpec(Filters: new Dictionary<string, string> { ["code"] = "eq:urn:x:1" }));
        list2.Items.Should().HaveCount(1);
        list2.Items[0]["name"]!.GetValue<string>().Should().Be("A");
    }

    // ---- Finding 9: delete semantics ----

    [Fact]
    public async Task SoftDelete_RemovesIndexRows()
    {
        using var provider = BuildProvider();
        await SeedWidgetAsync(provider);

        string id;
        using (var scope = provider.CreateScope())
        {
            var created = await Crud(scope).CreateAsync("Widget", new JsonObject { ["name"] = "A" });
            id = created["id"]!.GetValue<string>();

            var db = scope.ServiceProvider.GetRequiredService<DynDbContext>();
            (await db.Set<DsDynamicIndexRow>().CountAsync(i => i.RecordId == id)).Should().BeGreaterThan(0);

            await Crud(scope).DeleteAsync("Widget", id); // soft
            (await db.Set<DsDynamicIndexRow>().CountAsync(i => i.RecordId == id)).Should().Be(0,
                "index rows must be removed in the same SaveChanges as the soft delete");
        }
    }

    [Fact]
    public async Task HardDelete_PurgesSoftDeletedRecord_SoTheIdCanBeReused()
    {
        using var provider = BuildProvider();
        await SeedWidgetAsync(provider, keyClientSettable: true);

        var id = Guid.NewGuid().ToString("D");

        using var scope = provider.CreateScope();
        await Crud(scope).CreateAsync("Widget", new JsonObject { ["id"] = id, ["name"] = "First" });
        await Crud(scope).DeleteAsync("Widget", id); // soft delete

        // Hard delete must match the soft-deleted row so it can be purged.
        await Crud(scope).DeleteAsync("Widget", id, new CrudDeleteSpec(HardDelete: true));

        var db = scope.ServiceProvider.GetRequiredService<DynDbContext>();
        (await db.Set<DsDynamicRecordRow>().CountAsync(r => r.Id == id)).Should().Be(0);

        // And the id is reusable again.
        var recreated = await Crud(scope).CreateAsync("Widget", new JsonObject { ["id"] = id, ["name"] = "Again" });
        recreated["name"]!.GetValue<string>().Should().Be("Again");
    }

    // ---- Finding 10: disabled operation maps to CrudOperationDisabledException (400) ----

    [Fact]
    public async Task DisabledOperation_ThrowsCrudOperationDisabledException()
    {
        using var provider = BuildProvider();
        await SeedWidgetAsync(provider, enableDelete: false);

        using var scope = provider.CreateScope();
        var created = await Crud(scope).CreateAsync("Widget", new JsonObject { ["name"] = "A" });
        var id = created["id"]!.GetValue<string>();

        var act = () => Crud(scope).DeleteAsync("Widget", id);
        await act.Should().ThrowAsync<CrudOperationDisabledException>();
    }

    // ---- Finding 2: record + index rows persist atomically ----

    [Fact]
    public async Task Create_PersistsRecordAndIndexRows()
    {
        using var provider = BuildProvider();
        await SeedWidgetAsync(provider);

        using var scope = provider.CreateScope();
        var created = await Crud(scope).CreateAsync("Widget", new JsonObject { ["name"] = "A", ["code"] = "c1" });
        var id = created["id"]!.GetValue<string>();

        var db = scope.ServiceProvider.GetRequiredService<DynDbContext>();
        var indexed = await db.Set<DsDynamicIndexRow>()
            .Where(i => i.EntityKey == "Widget" && i.RecordId == id)
            .Select(i => i.PropertyApiName)
            .ToListAsync();

        indexed.Should().Contain("name");
        indexed.Should().Contain("code");
    }
}
