using DataSurface.Core.Contracts;
using FluentAssertions;
using Xunit;

namespace DataSurface.Tests.Unit.Core;

public class QueryContractTests
{
    [Fact]
    public void Constructor_WithAllParameters_SetsAllProperties()
    {
        var filterableFields = new[] { "title", "status", "createdAt" };
        var sortableFields = new[] { "title", "createdAt" };
        var searchableFields = new[] { "title", "description" };
        
        var contract = new QueryContract(
            MaxPageSize: 100,
            FilterableFields: filterableFields,
            SortableFields: sortableFields,
            SearchableFields: searchableFields,
            DefaultSort: "-createdAt"
        );
        
        contract.MaxPageSize.Should().Be(100);
        contract.FilterableFields.Should().BeEquivalentTo(filterableFields);
        contract.SortableFields.Should().BeEquivalentTo(sortableFields);
        contract.SearchableFields.Should().BeEquivalentTo(searchableFields);
        contract.DefaultSort.Should().Be("-createdAt");
    }

    [Fact]
    public void SearchableFields_WhenEmpty_ReturnsEmptyList()
    {
        var contract = new QueryContract(
            MaxPageSize: 50,
            FilterableFields: new[] { "id" },
            SortableFields: new[] { "id" },
            SearchableFields: Array.Empty<string>(),
            DefaultSort: null
        );
        
        contract.SearchableFields.Should().BeEmpty();
    }

    [Fact]
    public void SearchableFields_ContainsExpectedFields()
    {
        var searchableFields = new[] { "title", "description", "content" };
        
        var contract = new QueryContract(
            MaxPageSize: 50,
            FilterableFields: Array.Empty<string>(),
            SortableFields: Array.Empty<string>(),
            SearchableFields: searchableFields,
            DefaultSort: null
        );
        
        contract.SearchableFields.Should().HaveCount(3);
        contract.SearchableFields.Should().Contain("title");
        contract.SearchableFields.Should().Contain("description");
        contract.SearchableFields.Should().Contain("content");
    }

    [Fact]
    public void DefaultSort_WhenNull_ReturnsNull()
    {
        var contract = new QueryContract(50, Array.Empty<string>(), Array.Empty<string>(), Array.Empty<string>(), null);
        
        contract.DefaultSort.Should().BeNull();
    }
}
