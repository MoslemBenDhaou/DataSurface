using System.Text.Json.Nodes;
using DataSurface.Admin.DI;
using DataSurface.Admin.Dtos;
using DataSurface.Admin.Services;
using DataSurface.Admin.Validation;
using DataSurface.Core;
using DataSurface.Core.Contracts;
using DataSurface.Core.Enums;
using DataSurface.Dynamic.DI;
using DataSurface.Dynamic.Entities;
using DataSurface.Dynamic.Model;
using DataSurface.EFCore.Interfaces;
using DataSurface.EFCore.Providers;
using DataSurface.EFCore.Services;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DataSurface.Tests.Service.Dynamic;

/// <summary>
/// Tests for the dynamic metadata admin service and validator: entity-definition deletes must
/// purge the stored data, schema changes must trigger index rebuilds, imports must be
/// validate-all-then-apply, and the validator must reject malformed definitions.
/// </summary>
public class DynamicAdminServiceTests
{
    private sealed class DynDbContext : DbContext
    {
        public DynDbContext(DbContextOptions<DynDbContext> o) : base(o) { }
        protected override void OnModelCreating(ModelBuilder mb)
        {
            mb.AddDataSurfaceDynamic();
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
        services.AddDataSurfaceAdmin();
        return services.BuildServiceProvider();
    }

    private static AdminEntityDefDto WidgetDto(string entityKey = "Widget", string route = "widgets") => new()
    {
        EntityKey = entityKey,
        Route = route,
        Backend = StorageBackend.DynamicJson,
        KeyName = "id",
        KeyType = FieldType.Guid,
        Properties = new List<AdminPropertyDefDto>
        {
            new()
            {
                Name = "name", ApiName = "name", Type = FieldType.String,
                InFlags = CrudDto.Read | CrudDto.Create | CrudDto.Update | CrudDto.Filter | CrudDto.Sort
            }
        }
    };

    // ---- Finding 13: deleting an entity definition purges its records and index rows ----

    [Fact]
    public async Task DeleteEntity_PurgesRecordsAndIndexRows()
    {
        using var provider = BuildProvider();

        using var scope = provider.CreateScope();
        scope.ServiceProvider.GetRequiredService<DynDbContext>().Database.EnsureCreated();

        var admin = scope.ServiceProvider.GetRequiredService<DynamicMetadataAdminService>();
        var (_, errors) = await admin.UpsertEntityAsync(WidgetDto(), CancellationToken.None);
        errors.Should().BeEmpty();

        var crud = scope.ServiceProvider.GetRequiredService<IDataSurfaceCrudService>();
        await crud.CreateAsync("Widget", new JsonObject { ["name"] = "A" });
        await crud.CreateAsync("Widget", new JsonObject { ["name"] = "B" });

        var db = scope.ServiceProvider.GetRequiredService<DynDbContext>();
        (await db.Set<DsDynamicRecordRow>().CountAsync(r => r.EntityKey == "Widget")).Should().Be(2);
        (await db.Set<DsDynamicIndexRow>().CountAsync(r => r.EntityKey == "Widget")).Should().BeGreaterThan(0);

        var ok = await admin.DeleteEntityAsync("Widget", CancellationToken.None);
        ok.Should().BeTrue();

        (await db.Set<DsEntityDefRow>().CountAsync(r => r.EntityKey == "Widget")).Should().Be(0);
        (await db.Set<DsDynamicRecordRow>().CountAsync(r => r.EntityKey == "Widget")).Should().Be(0,
            "records must not survive an entity-definition delete to be resurrected later");
        (await db.Set<DsDynamicIndexRow>().CountAsync(r => r.EntityKey == "Widget")).Should().Be(0);
    }

    // ---- Finding 14: schema change triggers an automatic index rebuild ----

    [Fact]
    public async Task UpsertEntity_PropertyChange_RebuildsIndexes()
    {
        using var provider = BuildProvider();

        using var scope = provider.CreateScope();
        scope.ServiceProvider.GetRequiredService<DynDbContext>().Database.EnsureCreated();

        var admin = scope.ServiceProvider.GetRequiredService<DynamicMetadataAdminService>();
        (await admin.UpsertEntityAsync(WidgetDto(), CancellationToken.None)).Errors.Should().BeEmpty();

        var crud = scope.ServiceProvider.GetRequiredService<IDataSurfaceCrudService>();
        await crud.CreateAsync("Widget", new JsonObject { ["name"] = "A" });

        // Add a second filterable property; existing records must get reindexed automatically.
        var updated = await admin.GetEntityAsync("Widget", CancellationToken.None);
        updated!.Properties.Add(new AdminPropertyDefDto
        {
            Name = "name2", ApiName = "name2", Type = FieldType.String,
            InFlags = CrudDto.Read | CrudDto.Create | CrudDto.Update | CrudDto.Filter
        });

        var (_, errors) = await admin.UpsertEntityAsync(updated, CancellationToken.None);
        errors.Should().BeEmpty();

        // The original "name" index rows must still exist after the automatic rebuild.
        var db = scope.ServiceProvider.GetRequiredService<DynDbContext>();
        (await db.Set<DsDynamicIndexRow>().CountAsync(i => i.EntityKey == "Widget" && i.PropertyApiName == "name"))
            .Should().Be(1);
    }

    // ---- Finding 16: import validates everything before applying anything ----

    [Fact]
    public async Task Import_WithInvalidEntity_AppliesNothing()
    {
        using var provider = BuildProvider();

        using var scope = provider.CreateScope();
        scope.ServiceProvider.GetRequiredService<DynDbContext>().Database.EnsureCreated();

        var admin = scope.ServiceProvider.GetRequiredService<DynamicMetadataAdminService>();

        var bad = WidgetDto(entityKey: "Bad", route: "bad");
        bad.MaxPageSize = 0; // invalid

        var (imported, errors) = await admin.ImportAsync(new AdminImportPayloadDto
        {
            Entities = new List<AdminEntityDefDto> { WidgetDto(), bad }
        }, CancellationToken.None);

        imported.Should().Be(0, "validation failures must abort the import before anything is applied");
        errors.Should().ContainSingle(e => e.EntityKey == "Bad");

        var db = scope.ServiceProvider.GetRequiredService<DynDbContext>();
        (await db.Set<DsEntityDefRow>().CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task Import_AllValid_ImportsAll()
    {
        using var provider = BuildProvider();

        using var scope = provider.CreateScope();
        scope.ServiceProvider.GetRequiredService<DynDbContext>().Database.EnsureCreated();

        var admin = scope.ServiceProvider.GetRequiredService<DynamicMetadataAdminService>();
        var (imported, errors) = await admin.ImportAsync(new AdminImportPayloadDto
        {
            Entities = new List<AdminEntityDefDto> { WidgetDto(), WidgetDto(entityKey: "Gadget", route: "gadgets") }
        }, CancellationToken.None);

        errors.Should().BeEmpty();
        imported.Should().Be(2);
    }

    // ---- Finding 12: validator rules ----

    [Fact]
    public void Validator_RejectsEfCoreBackend()
    {
        var dto = WidgetDto();
        dto.Backend = StorageBackend.EfCore;

        var errors = DynamicMetadataValidator.Validate(dto);
        errors.Should().ContainKey("Backend");
    }

    [Fact]
    public void Validator_RejectsOutOfRangePagingAndExpandDepth()
    {
        var dto = WidgetDto();
        dto.MaxPageSize = 0;
        dto.MaxExpandDepth = 4;

        var errors = DynamicMetadataValidator.Validate(dto);
        errors.Should().ContainKey("MaxPageSize");
        errors.Should().ContainKey("MaxExpandDepth");
    }

    [Fact]
    public void Validator_RejectsRouteWithSlashOrWhitespace()
    {
        var dto = WidgetDto(route: "wid gets/x");
        var errors = DynamicMetadataValidator.Validate(dto);
        errors.Should().ContainKey("Route");
    }

    [Fact]
    public void Validator_RejectsKeyNameThatCannotBeAutoInjected()
    {
        var dto = WidgetDto();
        // No property NAME matches "Id", and the injected apiName "id" would collide with
        // an existing property apiName -> contract build would explode at runtime.
        dto.KeyName = "Id";
        dto.Properties.Add(new AdminPropertyDefDto
        {
            Name = "identifier", ApiName = "id", Type = FieldType.String, InFlags = CrudDto.Read
        });

        var errors = DynamicMetadataValidator.Validate(dto);
        errors.Should().ContainKey("KeyName");
    }

    [Fact]
    public void Validator_RejectsPropertyRelationApiNameCollision()
    {
        var dto = WidgetDto();
        dto.Relations.Add(new AdminRelationDefDto
        {
            Name = "name", ApiName = "name", Kind = RelationKind.OneToOne,
            TargetEntityKey = "Other", WriteMode = RelationWriteMode.NestedDisabled
        });

        var errors = DynamicMetadataValidator.Validate(dto);
        errors.Keys.Should().Contain(k => k.StartsWith("relations.name"));
    }

    [Fact]
    public void Validator_RejectsCollisionWithStaticResource()
    {
        // Build a static contract the cheap way: through the dynamic contract builder.
        var staticContract = new DynamicContractBuilder().Build(new EntityDef(
            EntityKey: "Widget",
            Route: "widgets",
            Backend: StorageBackend.EfCore,
            KeyName: "id",
            KeyType: FieldType.Guid,
            MaxPageSize: 100,
            MaxExpandDepth: 1,
            EnableList: true, EnableGet: true, EnableCreate: true, EnableUpdate: true, EnableDelete: true,
            Properties: new List<PropertyDef>(),
            Relations: new List<RelationDef>()));

        var staticProvider = new StaticResourceContractProvider(new[] { staticContract });

        var byKey = DynamicMetadataValidator.Validate(WidgetDto(entityKey: "Widget", route: "other"), staticProvider);
        byKey.Should().ContainKey("EntityKey");

        var byRoute = DynamicMetadataValidator.Validate(WidgetDto(entityKey: "Other", route: "widgets"), staticProvider);
        byRoute.Should().ContainKey("Route");
    }
}
