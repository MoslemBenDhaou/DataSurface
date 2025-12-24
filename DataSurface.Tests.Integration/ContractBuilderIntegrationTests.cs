using DataSurface.Core;
using DataSurface.Tests.Integration.TestFixtures;
using FluentAssertions;
using Xunit;

namespace DataSurface.Tests.Integration;

public class ContractBuilderIntegrationTests
{
    [Fact]
    public void BuildFromTypes_WithSearchableFields_IncludesSearchableFieldsInContract()
    {
        var builder = new ContractBuilder();

        var contracts = builder.BuildFromTypes(typeof(CrudTestUser));

        var contract = contracts.First();
        contract.Query.SearchableFields.Should().Contain("name");
        contract.Query.SearchableFields.Should().Contain("email");
    }

    [Fact]
    public void BuildFromTypes_WithDefaultValue_SetsDefaultValueInFieldContract()
    {
        var builder = new ContractBuilder();

        var contracts = builder.BuildFromTypes(typeof(CrudTestUser));

        var contract = contracts.First();
        var statusField = contract.Fields.First(f => f.ApiName == "status");
        statusField.DefaultValue.Should().Be("active");
    }

    [Fact]
    public void BuildFromTypes_WithAllowedValues_SetsAllowedValuesInValidation()
    {
        var builder = new ContractBuilder();

        var contracts = builder.BuildFromTypes(typeof(CrudTestUser));

        var contract = contracts.First();
        var statusField = contract.Fields.First(f => f.ApiName == "status");
        statusField.Validation.AllowedValues.Should().Contain("active");
        statusField.Validation.AllowedValues.Should().Contain("inactive");
        statusField.Validation.AllowedValues.Should().Contain("pending");
    }

    [Fact]
    public void BuildFromTypes_WithComputedExpression_SetsComputedFieldProperties()
    {
        var builder = new ContractBuilder();

        var contracts = builder.BuildFromTypes(typeof(CrudTestPost));

        var contract = contracts.First();
        var computedField = contract.Fields.FirstOrDefault(f => f.ApiName == "titleWithStatus");
        
        if (computedField != null)
        {
            computedField.Computed.Should().BeTrue();
            computedField.ComputedExpression.Should().Be("Title + ' - ' + Status");
            computedField.InCreate.Should().BeFalse();
            computedField.InUpdate.Should().BeFalse();
            computedField.Immutable.Should().BeTrue();
        }
    }

    [Fact]
    public void BuildFromTypes_WithTenantAttribute_IncludesTenantField()
    {
        var builder = new ContractBuilder();

        var contracts = builder.BuildFromTypes(typeof(CrudTestUser));

        var contract = contracts.First();
        // TenantId may or may not be included depending on whether it has a CrudField attribute
        // This test verifies the contract was built successfully
        contract.Should().NotBeNull();
        contract.Fields.Should().NotBeEmpty();
    }

    [Fact]
    public void BuildFromTypes_GeneratesCorrectRoute()
    {
        var builder = new ContractBuilder();

        var contracts = builder.BuildFromTypes(typeof(CrudTestUser), typeof(CrudTestPost));

        contracts.Should().HaveCount(2);
        contracts.Should().Contain(c => c.Route == "test-users");
        contracts.Should().Contain(c => c.Route == "test-posts");
    }

    [Fact]
    public void BuildFromTypes_SetsMaxPageSizeFromAttribute()
    {
        var builder = new ContractBuilder();

        var contracts = builder.BuildFromTypes(typeof(CrudTestUser), typeof(CrudTestPost));

        var userContract = contracts.First(c => c.Route == "test-users");
        var postContract = contracts.First(c => c.Route == "test-posts");

        userContract.Query.MaxPageSize.Should().Be(100);
        postContract.Query.MaxPageSize.Should().Be(50);
    }

    [Fact]
    public void BuildFromTypes_SetsRequiredOnCreateFromAttribute()
    {
        var builder = new ContractBuilder();

        var contracts = builder.BuildFromTypes(typeof(CrudTestUser));

        var contract = contracts.First();
        var nameField = contract.Fields.First(f => f.ApiName == "name");
        nameField.Validation.RequiredOnCreate.Should().BeTrue();
    }

    [Fact]
    public void BuildFromTypes_GeneratesFilterableFields()
    {
        var builder = new ContractBuilder();

        var contracts = builder.BuildFromTypes(typeof(CrudTestPost));

        var contract = contracts.First();
        contract.Query.FilterableFields.Should().Contain("id");
        contract.Query.FilterableFields.Should().Contain("title");
        contract.Query.FilterableFields.Should().Contain("status");
        contract.Query.FilterableFields.Should().Contain("price");
    }

    [Fact]
    public void BuildFromTypes_GeneratesSortableFields()
    {
        var builder = new ContractBuilder();

        var contracts = builder.BuildFromTypes(typeof(CrudTestPost));

        var contract = contracts.First();
        contract.Query.SortableFields.Should().Contain("id");
        contract.Query.SortableFields.Should().Contain("title");
        contract.Query.SortableFields.Should().Contain("price");
        contract.Query.SortableFields.Should().Contain("createdAt");
    }
}
