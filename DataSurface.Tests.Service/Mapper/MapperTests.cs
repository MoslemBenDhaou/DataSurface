using System.Text.Json.Nodes;
using DataSurface.Core.Contracts;
using DataSurface.Core.Enums;
using DataSurface.EFCore.Mapper;
using DataSurface.Tests.Service.Shared.Builders;
using DataSurface.Tests.Service.Shared.Entities;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace DataSurface.Tests.Service.Mapper;

/// <summary>
/// Tests for <see cref="EfCrudMapper"/>: JSON→Entity creation, JSON→Entity update,
/// default values, immutable field skip, and field filtering by operation shape.
/// </summary>
public class MapperTests : IDisposable
{
    private readonly CrudTestDbContext _db;
    private readonly EfCrudMapper _mapper = new();

    public MapperTests()
    {
        var options = new DbContextOptionsBuilder<CrudTestDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _db = new CrudTestDbContext(options);
        _db.Database.EnsureCreated();
    }

    public void Dispose() => _db.Dispose();

    private static ResourceContract BuildContract(FieldContract[]? extraFields = null)
    {
        var builder = new ResourceContractBuilder("SimpleItem", "simple-items")
            .Key("Id", FieldType.Int32)
            .WithField(new FieldBuilder("Id").OfType(FieldType.Int32).InRead().Filterable().Sortable().Build())
            .WithField(new FieldBuilder("Name").OfType(FieldType.String).ReadCreateUpdate().RequiredOnCreate().Build())
            .WithField(new FieldBuilder("Price").OfType(FieldType.Decimal).ReadCreateUpdate().Build());

        if (extraFields != null)
            foreach (var f in extraFields)
                builder.WithField(f);

        return builder.EnableAllOperations().Build();
    }

    // ────────────────────────────────────────────
    //  CreateEntity
    // ────────────────────────────────────────────

    [Fact]
    public void CreateEntity_MapsAllowedFieldsFromJson()
    {
        var contract = BuildContract();
        var body = new JsonObject { ["name"] = "Test", ["price"] = 42.5m };

        var entity = _mapper.CreateEntity<SimpleItem>(body, contract, _db);

        entity.Name.Should().Be("Test");
        entity.Price.Should().Be(42.5m);
    }

    [Fact]
    public void CreateEntity_IgnoresFieldsNotInInputShape()
    {
        var contract = BuildContract();
        // "id" is InRead only, not in Create input shape
        var body = new JsonObject { ["name"] = "Test", ["id"] = 999 };

        var entity = _mapper.CreateEntity<SimpleItem>(body, contract, _db);

        entity.Name.Should().Be("Test");
        entity.Id.Should().Be(0, "Id is not in create input shape and should be ignored");
    }

    [Fact]
    public void CreateEntity_MissingOptionalField_DefaultsToClrDefault()
    {
        var contract = BuildContract();
        var body = new JsonObject { ["name"] = "OnlyName" };

        var entity = _mapper.CreateEntity<SimpleItem>(body, contract, _db);

        entity.Name.Should().Be("OnlyName");
        entity.Price.Should().Be(0m, "missing optional field defaults to CLR default");
    }

    [Fact]
    public void CreateEntity_DefaultValue_AppliedWhenFieldNotProvided()
    {
        var defaultField = new FieldBuilder("IsActive")
            .OfType(FieldType.Boolean)
            .InCreate().InRead()
            .DefaultValue(true)
            .Build();

        var contract = BuildContract(new[] { defaultField });
        var body = new JsonObject { ["name"] = "WithDefault" };

        var entity = _mapper.CreateEntity<SimpleItem>(body, contract, _db);

        entity.IsActive.Should().BeTrue("default value should be applied when field is not provided");
    }

    [Fact]
    public void CreateEntity_DefaultValue_NotAppliedWhenFieldProvided()
    {
        var defaultField = new FieldBuilder("IsActive")
            .OfType(FieldType.Boolean)
            .InCreate().InRead()
            .DefaultValue(true)
            .Build();

        var contract = BuildContract(new[] { defaultField });
        var body = new JsonObject { ["name"] = "WithDefault", ["isActive"] = false };

        var entity = _mapper.CreateEntity<SimpleItem>(body, contract, _db);

        entity.IsActive.Should().BeFalse("explicit value should override default");
    }

    // ────────────────────────────────────────────
    //  ApplyUpdate
    // ────────────────────────────────────────────

    [Fact]
    public void ApplyUpdate_PatchesOnlyProvidedFields()
    {
        var contract = BuildContract();
        var entity = new SimpleItem { Id = 1, Name = "Original", Price = 10m };
        _db.SimpleItems.Add(entity);
        _db.SaveChanges();

        var patch = new JsonObject { ["name"] = "Updated" };

        _mapper.ApplyUpdate(entity, patch, contract, _db);

        entity.Name.Should().Be("Updated");
        entity.Price.Should().Be(10m, "price was not in patch, should stay unchanged");
    }

    [Fact]
    public void ApplyUpdate_ImmutableField_Skipped()
    {
        var builder = new ResourceContractBuilder("SimpleItem", "simple-items")
            .Key("Id", FieldType.Int32)
            .WithField(new FieldBuilder("Id").OfType(FieldType.Int32).InRead().Build())
            .WithField(new FieldBuilder("Name").OfType(FieldType.String).ReadCreateUpdate().RequiredOnCreate().Immutable().Build())
            .WithField(new FieldBuilder("Price").OfType(FieldType.Decimal).ReadCreateUpdate().Build())
            .EnableAllOperations();
        var contract = builder.Build();

        var entity = new SimpleItem { Id = 1, Name = "Original", Price = 10m };
        _db.SimpleItems.Add(entity);
        _db.SaveChanges();

        var patch = new JsonObject { ["name"] = "Changed", ["price"] = 20m };

        _mapper.ApplyUpdate(entity, patch, contract, _db);

        entity.Name.Should().Be("Original", "immutable field should not be changed on update");
        entity.Price.Should().Be(20m);
    }

    // ────────────────────────────────────────────
    //  Edge Cases
    // ────────────────────────────────────────────

    [Fact]
    public void CreateEntity_NullValueForNullableField_SetsNull()
    {
        var contract = BuildContract(new[]
        {
            new FieldBuilder("Description").OfType(FieldType.String).InCreate().InRead().Build()
        });

        var body = new JsonObject { ["name"] = "Test", ["description"] = null };

        var entity = _mapper.CreateEntity<SimpleItem>(body, contract, _db);

        entity.Description.Should().BeNull();
    }
}
