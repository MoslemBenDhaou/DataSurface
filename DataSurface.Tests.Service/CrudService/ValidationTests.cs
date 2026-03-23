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
/// Tests for field-level validation in the CRUD service:
/// AllowedValues, MinLength, MaxLength, Min, Max, Regex, required fields, immutable fields.
/// </summary>
public class ValidationTests : IDisposable
{
    private readonly TestServiceFactory _factory;
    private readonly CrudTestDbContext _db;

    private static ResourceContract BuildContract()
    {
        return new ResourceContractBuilder("SimpleItem", "simple-items")
            .Key("Id", FieldType.Int32)
            .WithField(new FieldBuilder("Id").OfType(FieldType.Int32).InRead().Filterable().Sortable().Build())
            .WithField(new FieldBuilder("Name").OfType(FieldType.String).ReadCreateUpdate()
                .RequiredOnCreate().MinLength(2).MaxLength(50).Build())
            .WithField(new FieldBuilder("Description").OfType(FieldType.String).Nullable()
                .InRead().InCreate().InUpdate().Regex(@"^[A-Za-z0-9\s]+$").Build())
            .WithField(new FieldBuilder("Price").OfType(FieldType.Decimal).ReadCreateUpdate()
                .RequiredOnCreate().Min(0.01m).Max(99999m).Build())
            .WithField(new FieldBuilder("IsActive").OfType(FieldType.Boolean).InRead().InCreate().InUpdate()
                .AllowedValues("true", "false").Build())
            .EnableAllOperations()
            .Build();
    }

    public ValidationTests()
    {
        var options = new DbContextOptionsBuilder<CrudTestDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _db = new CrudTestDbContext(options);
        _db.Database.EnsureCreated();
        _factory = new TestServiceFactory(_db, new[] { BuildContract() });
    }

    public void Dispose() => _factory.Dispose();

    // ────────────────────────────────────────────
    //  Required Fields
    // ────────────────────────────────────────────

    [Fact]
    public async Task Create_MissingAllRequiredFieldsFails()
    {
        var body = new JsonObject(); // missing name and price

        var act = () => _factory.CrudService.CreateAsync("SimpleItem", body);

        var ex = (await act.Should().ThrowAsync<CrudRequestValidationException>()).Which;
        ex.Errors.Should().ContainKey("name");
        ex.Errors.Should().ContainKey("price");
    }

    [Fact]
    public async Task Create_MissingSingleRequiredFieldFails()
    {
        var body = new JsonObject { ["name"] = "Valid" }; // missing price

        var act = () => _factory.CrudService.CreateAsync("SimpleItem", body);

        (await act.Should().ThrowAsync<CrudRequestValidationException>())
            .Which.Errors.Should().ContainKey("price");
    }

    [Fact]
    public async Task Create_AllRequiredFieldsPresentSucceeds()
    {
        var body = new JsonObject { ["name"] = "Valid Item", ["price"] = 10m };

        var result = await _factory.CrudService.CreateAsync("SimpleItem", body);

        result.Should().NotBeNull();
    }

    // ────────────────────────────────────────────
    //  MinLength / MaxLength
    // ────────────────────────────────────────────

    [Fact]
    public async Task Create_NameTooShortFails()
    {
        var body = new JsonObject { ["name"] = "X", ["price"] = 10m }; // min length 2

        var act = () => _factory.CrudService.CreateAsync("SimpleItem", body);

        (await act.Should().ThrowAsync<CrudRequestValidationException>())
            .Which.Errors.Should().ContainKey("name");
    }

    [Fact]
    public async Task Create_NameTooLongFails()
    {
        var body = new JsonObject { ["name"] = new string('A', 51), ["price"] = 10m }; // max length 50

        var act = () => _factory.CrudService.CreateAsync("SimpleItem", body);

        (await act.Should().ThrowAsync<CrudRequestValidationException>())
            .Which.Errors.Should().ContainKey("name");
    }

    [Fact]
    public async Task Create_NameExactMinLengthSucceeds()
    {
        var body = new JsonObject { ["name"] = "AB", ["price"] = 10m }; // exactly min length 2

        var result = await _factory.CrudService.CreateAsync("SimpleItem", body);

        result.Should().NotBeNull();
    }

    // ────────────────────────────────────────────
    //  Min / Max (numeric)
    // ────────────────────────────────────────────

    [Fact]
    public async Task Create_PriceBelowMinFails()
    {
        var body = new JsonObject { ["name"] = "Valid", ["price"] = 0m }; // min 0.01

        var act = () => _factory.CrudService.CreateAsync("SimpleItem", body);

        (await act.Should().ThrowAsync<CrudRequestValidationException>())
            .Which.Errors.Should().ContainKey("price");
    }

    [Fact]
    public async Task Create_PriceAboveMaxFails()
    {
        var body = new JsonObject { ["name"] = "Valid", ["price"] = 100000m }; // max 99999

        var act = () => _factory.CrudService.CreateAsync("SimpleItem", body);

        (await act.Should().ThrowAsync<CrudRequestValidationException>())
            .Which.Errors.Should().ContainKey("price");
    }

    [Fact]
    public async Task Create_PriceExactMinSucceeds()
    {
        var body = new JsonObject { ["name"] = "Valid", ["price"] = 0.01m };

        var result = await _factory.CrudService.CreateAsync("SimpleItem", body);

        result.Should().NotBeNull();
    }

    // ────────────────────────────────────────────
    //  Regex
    // ────────────────────────────────────────────

    [Fact]
    public async Task Create_DescriptionFailingRegexFails()
    {
        var body = new JsonObject { ["name"] = "Valid", ["price"] = 10m, ["description"] = "Invalid!@#$" };

        var act = () => _factory.CrudService.CreateAsync("SimpleItem", body);

        (await act.Should().ThrowAsync<CrudRequestValidationException>())
            .Which.Errors.Should().ContainKey("description");
    }

    [Fact]
    public async Task Create_DescriptionPassingRegexSucceeds()
    {
        var body = new JsonObject { ["name"] = "Valid", ["price"] = 10m, ["description"] = "AlphaNumeric 123" };

        var result = await _factory.CrudService.CreateAsync("SimpleItem", body);

        result.Should().NotBeNull();
    }

    // ────────────────────────────────────────────
    //  Immutable Fields on Update
    // ────────────────────────────────────────────

    [Fact]
    public async Task Update_SettingImmutableIdFieldFails()
    {
        _db.SimpleItems.Add(new SimpleItem { Name = "Test", Price = 10 });
        await _db.SaveChangesAsync();
        var id = _db.SimpleItems.First().Id;

        var patch = new JsonObject { ["id"] = 999 };
        var act = () => _factory.CrudService.UpdateAsync("SimpleItem", id, patch);

        (await act.Should().ThrowAsync<CrudRequestValidationException>())
            .Which.Errors.Should().ContainKey("id");
    }

    // ────────────────────────────────────────────
    //  Unknown Fields
    // ────────────────────────────────────────────

    [Fact]
    public async Task Create_UnknownFieldFails()
    {
        var body = new JsonObject { ["name"] = "Valid", ["price"] = 10m, ["bogus"] = "nope" };

        var act = () => _factory.CrudService.CreateAsync("SimpleItem", body);

        (await act.Should().ThrowAsync<CrudRequestValidationException>())
            .Which.Errors.Should().ContainKey("bogus");
    }

    [Fact]
    public async Task Update_UnknownFieldFails()
    {
        _db.SimpleItems.Add(new SimpleItem { Name = "Test", Price = 10 });
        await _db.SaveChangesAsync();
        var id = _db.SimpleItems.First().Id;

        var patch = new JsonObject { ["notAField"] = "nope" };
        var act = () => _factory.CrudService.UpdateAsync("SimpleItem", id, patch);

        (await act.Should().ThrowAsync<CrudRequestValidationException>())
            .Which.Errors.Should().ContainKey("notAField");
    }

    // ────────────────────────────────────────────
    //  Multiple Validation Errors at Once
    // ────────────────────────────────────────────

    [Fact]
    public async Task Create_MultipleViolationsReturnsAllErrors()
    {
        // Missing name (required), price below min, description fails regex
        var body = new JsonObject { ["price"] = 0m, ["description"] = "bad!@#" };

        var act = () => _factory.CrudService.CreateAsync("SimpleItem", body);

        var ex = (await act.Should().ThrowAsync<CrudRequestValidationException>()).Which;
        ex.Errors.Should().HaveCountGreaterOrEqualTo(2);
    }

    // ────────────────────────────────────────────
    //  Update Validation Also Enforced
    // ────────────────────────────────────────────

    [Fact]
    public async Task Update_NameTooShortFails()
    {
        _db.SimpleItems.Add(new SimpleItem { Name = "Test", Price = 10 });
        await _db.SaveChangesAsync();
        var id = _db.SimpleItems.First().Id;

        var patch = new JsonObject { ["name"] = "X" }; // min 2

        var act = () => _factory.CrudService.UpdateAsync("SimpleItem", id, patch);

        (await act.Should().ThrowAsync<CrudRequestValidationException>())
            .Which.Errors.Should().ContainKey("name");
    }
}
