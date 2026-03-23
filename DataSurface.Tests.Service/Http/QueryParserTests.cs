using DataSurface.Core.Contracts;
using DataSurface.Core.Enums;
using DataSurface.Http;
using DataSurface.Tests.Service.Shared.Builders;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Primitives;
using Xunit;

namespace DataSurface.Tests.Service.Http;

/// <summary>
/// Tests for <see cref="DataSurfaceQueryParser"/>: verifies parsing of paging,
/// sorting, filtering, search, field projection, and expand parameters.
/// Covers strategy §4.9 query parsing.
/// </summary>
public class QueryParserTests
{
    private static ResourceContract BuildContract()
    {
        return new ResourceContractBuilder("SimpleItem", "simple-items")
            .Key("Id", FieldType.Int32)
            .WithField(new FieldBuilder("Id").OfType(FieldType.Int32).InRead().Filterable().Sortable().Build())
            .WithField(new FieldBuilder("Name").OfType(FieldType.String).ReadCreateUpdate().Filterable().Sortable().Searchable().Build())
            .WithField(new FieldBuilder("Price").OfType(FieldType.Decimal).ReadCreateUpdate().Filterable().Build())
            .WithExpandAllowed("orders", "category")
            .EnableAllOperations()
            .Build();
    }

    private static HttpRequest CreateRequest(Dictionary<string, StringValues>? query = null)
    {
        var context = new DefaultHttpContext();
        if (query != null)
            context.Request.Query = new QueryCollection(query);
        return context.Request;
    }

    // ────────────────────────────────────────────
    //  Paging
    // ────────────────────────────────────────────

    [Fact]
    public void ParseQuerySpec_NoParams_ReturnsDefaults()
    {
        var req = CreateRequest();
        var spec = DataSurfaceQueryParser.ParseQuerySpec(req, BuildContract());

        spec.Page.Should().Be(1);
        spec.PageSize.Should().Be(20);
        spec.Sort.Should().BeNull();
        spec.Filters.Should().BeEmpty();
        spec.Search.Should().BeNull();
        spec.Fields.Should().BeNull();
    }

    [Fact]
    public void ParseQuerySpec_PageAndPageSize_Parsed()
    {
        var req = CreateRequest(new()
        {
            ["page"] = "3",
            ["pageSize"] = "50"
        });

        var spec = DataSurfaceQueryParser.ParseQuerySpec(req, BuildContract());

        spec.Page.Should().Be(3);
        spec.PageSize.Should().Be(50);
    }

    [Fact]
    public void ParseQuerySpec_InvalidPage_FallsBackToDefault()
    {
        var req = CreateRequest(new()
        {
            ["page"] = "abc",
            ["pageSize"] = "xyz"
        });

        var spec = DataSurfaceQueryParser.ParseQuerySpec(req, BuildContract());

        spec.Page.Should().Be(1);
        spec.PageSize.Should().Be(20);
    }

    // ────────────────────────────────────────────
    //  Sorting
    // ────────────────────────────────────────────

    [Fact]
    public void ParseQuerySpec_Sort_Parsed()
    {
        var req = CreateRequest(new() { ["sort"] = "-name,price" });

        var spec = DataSurfaceQueryParser.ParseQuerySpec(req, BuildContract());

        spec.Sort.Should().Be("-name,price");
    }

    // ────────────────────────────────────────────
    //  Filtering
    // ────────────────────────────────────────────

    [Fact]
    public void ParseQuerySpec_SingleFilter_Parsed()
    {
        var req = CreateRequest(new()
        {
            ["filter[name]"] = "eq:Test"
        });

        var spec = DataSurfaceQueryParser.ParseQuerySpec(req, BuildContract());

        spec.Filters.Should().ContainKey("name");
        spec.Filters!["name"].Should().Be("eq:Test");
    }

    [Fact]
    public void ParseQuerySpec_MultipleFilters_Parsed()
    {
        var req = CreateRequest(new()
        {
            ["filter[name]"] = "contains:Widget",
            ["filter[price]"] = "gte:10"
        });

        var spec = DataSurfaceQueryParser.ParseQuerySpec(req, BuildContract());

        spec.Filters.Should().HaveCount(2);
        spec.Filters!["name"].Should().Be("contains:Widget");
        spec.Filters!["price"].Should().Be("gte:10");
    }

    [Fact]
    public void ParseQuerySpec_EmptyFilterValue_Ignored()
    {
        var req = CreateRequest(new()
        {
            ["filter[name]"] = ""
        });

        var spec = DataSurfaceQueryParser.ParseQuerySpec(req, BuildContract());

        spec.Filters.Should().BeEmpty();
    }

    // ────────────────────────────────────────────
    //  Full-Text Search
    // ────────────────────────────────────────────

    [Fact]
    public void ParseQuerySpec_SearchParam_Parsed()
    {
        var req = CreateRequest(new() { ["q"] = "widget" });

        var spec = DataSurfaceQueryParser.ParseQuerySpec(req, BuildContract());

        spec.Search.Should().Be("widget");
    }

    // ────────────────────────────────────────────
    //  Field Projection
    // ────────────────────────────────────────────

    [Fact]
    public void ParseQuerySpec_FieldsParam_Parsed()
    {
        var req = CreateRequest(new() { ["fields"] = "name,price" });

        var spec = DataSurfaceQueryParser.ParseQuerySpec(req, BuildContract());

        spec.Fields.Should().Be("name,price");
    }

    // ────────────────────────────────────────────
    //  Expand
    // ────────────────────────────────────────────

    [Fact]
    public void ParseExpand_ValidExpand_ReturnsSpec()
    {
        var req = CreateRequest(new() { ["expand"] = "orders,category" });

        var expand = DataSurfaceQueryParser.ParseExpand(req, BuildContract());

        expand.Should().NotBeNull();
        expand!.Expand.Should().BeEquivalentTo(new[] { "orders", "category" });
    }

    [Fact]
    public void ParseExpand_FilteredToAllowed_DiscardsUnknown()
    {
        var req = CreateRequest(new() { ["expand"] = "orders,unknown,category" });

        var expand = DataSurfaceQueryParser.ParseExpand(req, BuildContract());

        expand.Should().NotBeNull();
        expand!.Expand.Should().BeEquivalentTo(new[] { "orders", "category" });
    }

    [Fact]
    public void ParseExpand_AllUnknown_ReturnsNull()
    {
        var req = CreateRequest(new() { ["expand"] = "foo,bar" });

        var expand = DataSurfaceQueryParser.ParseExpand(req, BuildContract());

        expand.Should().BeNull();
    }

    [Fact]
    public void ParseExpand_EmptyParam_ReturnsNull()
    {
        var req = CreateRequest(new() { ["expand"] = "" });

        var expand = DataSurfaceQueryParser.ParseExpand(req, BuildContract());

        expand.Should().BeNull();
    }

    [Fact]
    public void ParseExpand_NotPresent_ReturnsNull()
    {
        var req = CreateRequest();

        var expand = DataSurfaceQueryParser.ParseExpand(req, BuildContract());

        expand.Should().BeNull();
    }
}
