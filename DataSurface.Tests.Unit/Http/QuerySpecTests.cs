using DataSurface.EFCore.Contracts;
using FluentAssertions;
using Xunit;

namespace DataSurface.Tests.Unit.Http;

public class QuerySpecTests
{
    [Fact]
    public void DefaultValues_AreCorrect()
    {
        var spec = new QuerySpec();

        spec.Page.Should().Be(1);
        spec.PageSize.Should().Be(20);
        spec.Sort.Should().BeNull();
        spec.Filters.Should().BeNull();
        spec.Search.Should().BeNull();
        spec.Fields.Should().BeNull();
    }

    [Fact]
    public void Constructor_WithAllParameters_SetsAllProperties()
    {
        var filters = new Dictionary<string, string> { ["status"] = "active" };

        var spec = new QuerySpec(
            Page: 2,
            PageSize: 50,
            Sort: "-createdAt",
            Filters: filters,
            Search: "test query",
            Fields: "id,title,status"
        );

        spec.Page.Should().Be(2);
        spec.PageSize.Should().Be(50);
        spec.Sort.Should().Be("-createdAt");
        spec.Filters.Should().BeEquivalentTo(filters);
        spec.Search.Should().Be("test query");
        spec.Fields.Should().Be("id,title,status");
    }

    [Fact]
    public void Search_WhenSet_ReturnsSetValue()
    {
        var spec = new QuerySpec(Search: "hello world");

        spec.Search.Should().Be("hello world");
    }

    [Fact]
    public void Fields_WhenSet_ReturnsSetValue()
    {
        var spec = new QuerySpec(Fields: "id,name,email");

        spec.Fields.Should().Be("id,name,email");
    }

    [Fact]
    public void WithExpression_CreatesNewInstanceWithModifiedValue()
    {
        var original = new QuerySpec(Page: 1, PageSize: 20);
        var modified = original with { PageSize = 50 };

        original.PageSize.Should().Be(20);
        modified.PageSize.Should().Be(50);
        modified.Page.Should().Be(1);
    }

    [Fact]
    public void WithExpression_CanModifySearch()
    {
        var original = new QuerySpec();
        var modified = original with { Search = "new search" };

        original.Search.Should().BeNull();
        modified.Search.Should().Be("new search");
    }

    [Fact]
    public void WithExpression_CanModifyFields()
    {
        var original = new QuerySpec();
        var modified = original with { Fields = "id,title" };

        original.Fields.Should().BeNull();
        modified.Fields.Should().Be("id,title");
    }
}
