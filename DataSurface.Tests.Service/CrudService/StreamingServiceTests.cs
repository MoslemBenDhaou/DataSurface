using System.Text.Json.Nodes;
using DataSurface.Core.Contracts;
using DataSurface.Core.Enums;
using DataSurface.EFCore.Contracts;
using DataSurface.EFCore.Services;
using DataSurface.Tests.Service.Shared;
using DataSurface.Tests.Service.Shared.Builders;
using DataSurface.Tests.Service.Shared.Entities;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace DataSurface.Tests.Service.CrudService;

/// <summary>
/// Tests for <see cref="EfDataSurfaceStreamingService"/>.
/// </summary>
public class StreamingServiceTests : IDisposable
{
    private readonly TestServiceFactory _factory;
    private readonly CrudTestDbContext _db;
    private readonly EfDataSurfaceStreamingService _streaming;

    private static ResourceContract BuildContract()
    {
        return new ResourceContractBuilder("SimpleItem", "simple-items")
            .Key("Id", FieldType.Int32)
            .MaxPageSize(5) // small page for streaming pagination tests
            .WithField(new FieldBuilder("Id").OfType(FieldType.Int32).InRead().Filterable().Sortable().Build())
            .WithField(new FieldBuilder("Name").OfType(FieldType.String).ReadCreateUpdate().RequiredOnCreate().Searchable().Build())
            .WithField(new FieldBuilder("Price").OfType(FieldType.Decimal).ReadCreateUpdate().Build())
            .EnableAllOperations()
            .Build();
    }

    public StreamingServiceTests()
    {
        var options = new DbContextOptionsBuilder<CrudTestDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _db = new CrudTestDbContext(options);
        _db.Database.EnsureCreated();
        _factory = new TestServiceFactory(_db, new[] { BuildContract() });

        _streaming = new EfDataSurfaceStreamingService(
            _factory.CrudService,
            _factory.Contracts,
            NullLogger<EfDataSurfaceStreamingService>.Instance);
    }

    public void Dispose() => _factory.Dispose();

    private void SeedItems(int count)
    {
        for (int i = 1; i <= count; i++)
            _db.SimpleItems.Add(new SimpleItem { Name = $"Item{i}", Price = i * 10m });
        _db.SaveChanges();
    }

    // ────────────────────────────────────────────
    //  StreamAsync
    // ────────────────────────────────────────────

    [Fact]
    public async Task StreamAsync_EmptyTable_YieldsNothing()
    {
        var items = new List<JsonObject>();
        await foreach (var item in _streaming.StreamAsync("SimpleItem", new QuerySpec()))
            items.Add(item);

        items.Should().BeEmpty();
    }

    [Fact]
    public async Task StreamAsync_SinglePage_YieldsAllItems()
    {
        SeedItems(3);

        var items = new List<JsonObject>();
        await foreach (var item in _streaming.StreamAsync("SimpleItem", new QuerySpec()))
            items.Add(item);

        items.Should().HaveCount(3);
    }

    [Fact]
    public async Task StreamAsync_MultiplePages_YieldsAllItems()
    {
        SeedItems(12); // 3 pages with MaxPageSize=5 (5+5+2)

        var items = new List<JsonObject>();
        await foreach (var item in _streaming.StreamAsync("SimpleItem", new QuerySpec()))
            items.Add(item);

        items.Should().HaveCount(12);
    }

    [Fact]
    public async Task StreamAsync_ExactPageBoundary_YieldsAllItems()
    {
        SeedItems(10); // 2 full pages with MaxPageSize=5

        var items = new List<JsonObject>();
        await foreach (var item in _streaming.StreamAsync("SimpleItem", new QuerySpec()))
            items.Add(item);

        items.Should().HaveCount(10);
    }

    // ────────────────────────────────────────────
    //  StreamBatchesAsync
    // ────────────────────────────────────────────

    [Fact]
    public async Task StreamBatchesAsync_EmptyTable_YieldsNoBatches()
    {
        var batches = new List<IReadOnlyList<JsonObject>>();
        await foreach (var batch in _streaming.StreamBatchesAsync("SimpleItem", new QuerySpec(), batchSize: 5))
            batches.Add(batch);

        batches.Should().BeEmpty();
    }

    [Fact]
    public async Task StreamBatchesAsync_MultipleBatches_YieldsCorrectCounts()
    {
        SeedItems(12); // batchSize=5: batches of 5, 5, 2

        var batches = new List<IReadOnlyList<JsonObject>>();
        await foreach (var batch in _streaming.StreamBatchesAsync("SimpleItem", new QuerySpec(), batchSize: 5))
            batches.Add(batch);

        batches.Should().HaveCount(3);
        batches[0].Should().HaveCount(5);
        batches[1].Should().HaveCount(5);
        batches[2].Should().HaveCount(2);
    }

    [Fact]
    public async Task StreamBatchesAsync_SingleBatch_YieldsOneBatch()
    {
        SeedItems(3);

        var batches = new List<IReadOnlyList<JsonObject>>();
        await foreach (var batch in _streaming.StreamBatchesAsync("SimpleItem", new QuerySpec(), batchSize: 5))
            batches.Add(batch);

        batches.Should().HaveCount(1);
        batches[0].Should().HaveCount(3);
    }

    // ────────────────────────────────────────────
    //  Cancellation
    // ────────────────────────────────────────────

    [Fact]
    public async Task StreamAsync_CancellationStopsEarly()
    {
        SeedItems(20);
        using var cts = new CancellationTokenSource();

        var items = new List<JsonObject>();
        await foreach (var item in _streaming.StreamAsync("SimpleItem", new QuerySpec(), ct: cts.Token))
        {
            items.Add(item);
            if (items.Count >= 5)
                cts.Cancel();
        }

        // Should have stopped after first page (5 items) since cancellation happens after enumeration
        items.Count.Should().BeLessOrEqualTo(10); // at most 2 pages before cancel takes effect
    }
}
