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
/// Tests for concurrency token validation during update and delete operations.
/// </summary>
public class ConcurrencyTests : IDisposable
{
    private readonly TestServiceFactory _factory;
    private readonly CrudTestDbContext _db;

    private static ResourceContract BuildVersionedContract()
    {
        var concurrency = new ConcurrencyContract(ConcurrencyMode.RowVersion, "rowVersion", RequiredOnUpdate: true);

        return new ResourceContractBuilder("VersionedItem", "versioned-items")
            .Key("Id", FieldType.Int32)
            .MaxPageSize(100)
            .WithField(new FieldBuilder("Id").OfType(FieldType.Int32).InRead().Filterable().Sortable().Immutable().Build())
            .WithField(new FieldBuilder("Name").OfType(FieldType.String).ReadCreateUpdate().RequiredOnCreate().Build())
            .WithField(new FieldBuilder("RowVersion").OfType(FieldType.String).InRead().InUpdate().Build())
            .WithConcurrency(concurrency)
            .EnableAllOperations()
            .Build();
    }

    public ConcurrencyTests()
    {
        var options = new DbContextOptionsBuilder<CrudTestDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _db = new CrudTestDbContext(options);
        _db.Database.EnsureCreated();
        _factory = new TestServiceFactory(_db, new[] { BuildVersionedContract() });
    }

    public void Dispose() => _factory.Dispose();

    // ────────────────────────────────────────────
    //  Update: Concurrency Token Required
    // ────────────────────────────────────────────

    private static readonly byte[] TokenV1 = new byte[] { 1, 0, 0, 0 };
    private static string TokenV1Base64 => Convert.ToBase64String(TokenV1);

    [Fact]
    public async Task UpdateAsync_MissingConcurrencyTokenThrowsValidation()
    {
        _db.VersionedItems.Add(new VersionedItem { Name = "Original", RowVersion = TokenV1 });
        await _db.SaveChangesAsync();
        var id = _db.VersionedItems.First().Id;

        var patch = new JsonObject { ["name"] = "Updated" }; // missing rowVersion

        var act = () => _factory.CrudService.UpdateAsync("VersionedItem", id, patch);

        (await act.Should().ThrowAsync<CrudRequestValidationException>())
            .Which.Errors.Should().ContainKey("rowVersion");
    }

    [Fact]
    public async Task UpdateAsync_WithConcurrencyTokenSucceeds()
    {
        _db.VersionedItems.Add(new VersionedItem { Name = "Original", RowVersion = TokenV1 });
        await _db.SaveChangesAsync();
        var id = _db.VersionedItems.First().Id;

        var patch = new JsonObject { ["name"] = "Updated", ["rowVersion"] = TokenV1Base64 };

        var result = await _factory.CrudService.UpdateAsync("VersionedItem", id, patch);

        result["name"]!.GetValue<string>().Should().Be("Updated");
    }

    // ────────────────────────────────────────────
    //  Delete: Concurrency Token Check
    // ────────────────────────────────────────────

    [Fact]
    public async Task DeleteAsync_MismatchedConcurrencyTokenThrows()
    {
        _db.VersionedItems.Add(new VersionedItem { Name = "ToDelete", RowVersion = TokenV1 });
        await _db.SaveChangesAsync();
        var id = _db.VersionedItems.First().Id;

        var deleteSpec = new CrudDeleteSpec(ConcurrencyToken: "wrong_token");

        var act = () => _factory.CrudService.DeleteAsync("VersionedItem", id, deleteSpec);

        await act.Should().ThrowAsync<CrudConcurrencyException>();
    }

    [Fact]
    public async Task DeleteAsync_MatchingConcurrencyTokenSucceeds()
    {
        _db.VersionedItems.Add(new VersionedItem { Name = "ToDelete", RowVersion = TokenV1 });
        await _db.SaveChangesAsync();
        var id = _db.VersionedItems.First().Id;

        var deleteSpec = new CrudDeleteSpec(ConcurrencyToken: TokenV1Base64);

        await _factory.CrudService.DeleteAsync("VersionedItem", id, deleteSpec);

        _db.VersionedItems.Find(id).Should().BeNull();
    }

    [Fact]
    public async Task DeleteAsync_NoConcurrencyTokenSkipsCheck()
    {
        _db.VersionedItems.Add(new VersionedItem { Name = "NoConcCheck", RowVersion = TokenV1 });
        await _db.SaveChangesAsync();
        var id = _db.VersionedItems.First().Id;

        // No ConcurrencyToken in deleteSpec → should succeed without check
        await _factory.CrudService.DeleteAsync("VersionedItem", id);

        _db.VersionedItems.Find(id).Should().BeNull();
    }

    // ────────────────────────────────────────────
    //  Create: No concurrency needed
    // ────────────────────────────────────────────

    [Fact]
    public async Task CreateAsync_DoesNotRequireConcurrencyToken()
    {
        var body = new JsonObject { ["name"] = "NewVersioned" };

        var result = await _factory.CrudService.CreateAsync("VersionedItem", body);

        result.Should().NotBeNull();
        result["name"]!.GetValue<string>().Should().Be("NewVersioned");
    }

    // ────────────────────────────────────────────
    //  B2: read-only concurrency token (the realistic [CrudConcurrency][CrudField(Read)]
    //  shape) must be accepted on update and round-trip as base64 on read.
    // ────────────────────────────────────────────

    private static ResourceContract BuildReadOnlyTokenContract()
    {
        // RowVersion is read-only: NOT part of the update input shape (unlike
        // BuildVersionedContract, which marks it InUpdate to dodge the bug).
        var concurrency = new ConcurrencyContract(ConcurrencyMode.RowVersion, "rowVersion", RequiredOnUpdate: true);

        return new ResourceContractBuilder("VersionedItem", "versioned-items")
            .Key("Id", FieldType.Int32)
            .WithField(new FieldBuilder("Id").OfType(FieldType.Int32).InRead().Immutable().Build())
            .WithField(new FieldBuilder("Name").OfType(FieldType.String).ReadCreateUpdate().RequiredOnCreate().Build())
            .WithField(new FieldBuilder("RowVersion").OfType(FieldType.String).ReadOnly().Build())
            .WithConcurrency(concurrency)
            .EnableAllOperations()
            .Build();
    }

    [Fact]
    public async Task UpdateAsync_ReadOnlyConcurrencyToken_IsAcceptedAndRoundTripsAsBase64()
    {
        using var db = new CrudTestDbContext(new DbContextOptionsBuilder<CrudTestDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        db.Database.EnsureCreated();
        using var factory = new TestServiceFactory(db, new[] { BuildReadOnlyTokenContract() });

        db.VersionedItems.Add(new VersionedItem { Name = "Original", RowVersion = TokenV1 });
        await db.SaveChangesAsync();
        var id = db.VersionedItems.First().Id;

        // B2b: a byte[] rowversion must serialize as base64 on read.
        var read = await factory.CrudService.GetAsync("VersionedItem", id);
        read!["rowVersion"]!.GetValue<string>().Should().Be(TokenV1Base64);

        // B2a: the token is not in the update input shape, but must still be accepted
        // (previously rejected as "Field is not allowed").
        var patch = new JsonObject { ["name"] = "Updated", ["rowVersion"] = TokenV1Base64 };
        var result = await factory.CrudService.UpdateAsync("VersionedItem", id, patch);

        result["name"]!.GetValue<string>().Should().Be("Updated");
    }
}
