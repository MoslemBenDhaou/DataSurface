using System.Text.Json.Nodes;
using DataSurface.Core.Contracts;
using DataSurface.Core.Enums;
using DataSurface.EFCore.Contracts;
using DataSurface.EFCore.Exceptions;
using DataSurface.EFCore.Options;
using DataSurface.Tests.Service.Shared;
using DataSurface.Tests.Service.Shared.Builders;
using DataSurface.Tests.Service.Shared.Entities;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DataSurface.Tests.Service.CrudService;

/// <summary>
/// P3: opt-in strict query validation rejects disallowed filter/sort fields (HTTP 400) instead of
/// silently ignoring them; the default (forgiving) behavior is preserved.
/// </summary>
public class StrictQueryTests : IDisposable
{
    private readonly CrudTestDbContext _db;

    public StrictQueryTests()
    {
        var options = new DbContextOptionsBuilder<CrudTestDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _db = new CrudTestDbContext(options);
        _db.Database.EnsureCreated();
    }

    public void Dispose() => _db.Dispose();

    private static ResourceContract Contract()
        => new ResourceContractBuilder("SimpleItem", "simple-items")
            .Key("Id", FieldType.Int32)
            .WithField(new FieldBuilder("Id").OfType(FieldType.Int32).InRead().Filterable().Sortable().Build())
            .WithField(new FieldBuilder("Name").OfType(FieldType.String).ReadCreateUpdate().Filterable().Sortable().RequiredOnCreate().Build())
            // Price is readable/writable but NOT filterable or sortable.
            .WithField(new FieldBuilder("Price").OfType(FieldType.Decimal).ReadCreateUpdate().Build())
            .EnableAllOperations()
            .Build();

    [Fact]
    public async Task StrictQuery_DisallowedFilter_ThrowsValidation()
    {
        using var factory = new TestServiceFactory(_db, new[] { Contract() }, svc =>
            svc.AddSingleton(new DataSurfaceEfCoreOptions { StrictQuery = true }));

        var spec = new QuerySpec(Filters: new Dictionary<string, string> { ["price"] = "eq:5" });

        var act = () => factory.CrudService.ListAsync("SimpleItem", spec);

        await act.Should().ThrowAsync<CrudRequestValidationException>();
    }

    [Fact]
    public async Task NonStrict_DisallowedFilter_IsSilentlyIgnored()
    {
        _db.SimpleItems.Add(new SimpleItem { Name = "A", Price = 1m });
        await _db.SaveChangesAsync();

        using var factory = new TestServiceFactory(_db, new[] { Contract() }); // default: non-strict

        var spec = new QuerySpec(Filters: new Dictionary<string, string> { ["price"] = "eq:999" });

        var result = await factory.CrudService.ListAsync("SimpleItem", spec);

        result.Items.Should().HaveCount(1, "the disallowed filter is ignored when StrictQuery is off");
    }
}
