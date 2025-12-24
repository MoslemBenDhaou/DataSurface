using DataSurface.Core.Contracts;
using FluentAssertions;
using Xunit;

namespace DataSurface.Tests.Unit.Core;

public class FieldValidationContractTests
{
    [Fact]
    public void Constructor_WithAllParameters_SetsAllProperties()
    {
        var allowedValues = new[] { "Active", "Inactive", "Pending" };
        
        var contract = new FieldValidationContract(
            RequiredOnCreate: true,
            MinLength: 1,
            MaxLength: 100,
            Min: 0,
            Max: 1000,
            Regex: @"^\w+$",
            AllowedValues: allowedValues
        );
        
        contract.RequiredOnCreate.Should().BeTrue();
        contract.MinLength.Should().Be(1);
        contract.MaxLength.Should().Be(100);
        contract.Min.Should().Be(0);
        contract.Max.Should().Be(1000);
        contract.Regex.Should().Be(@"^\w+$");
        contract.AllowedValues.Should().BeEquivalentTo(allowedValues);
    }

    [Fact]
    public void Constructor_WithNullOptionalParameters_SetsNullValues()
    {
        var contract = new FieldValidationContract(
            RequiredOnCreate: false,
            MinLength: null,
            MaxLength: null,
            Min: null,
            Max: null,
            Regex: null,
            AllowedValues: null
        );
        
        contract.RequiredOnCreate.Should().BeFalse();
        contract.MinLength.Should().BeNull();
        contract.MaxLength.Should().BeNull();
        contract.Min.Should().BeNull();
        contract.Max.Should().BeNull();
        contract.Regex.Should().BeNull();
        contract.AllowedValues.Should().BeNull();
    }

    [Fact]
    public void AllowedValues_DefaultValue_IsNull()
    {
        var contract = new FieldValidationContract(false, null, null, null, null, null);
        
        contract.AllowedValues.Should().BeNull();
    }

    [Fact]
    public void AllowedValues_WhenSet_ContainsExpectedValues()
    {
        var allowedValues = new List<string> { "draft", "published", "archived" };
        
        var contract = new FieldValidationContract(false, null, null, null, null, null, allowedValues);
        
        contract.AllowedValues.Should().HaveCount(3);
        contract.AllowedValues.Should().Contain("draft");
        contract.AllowedValues.Should().Contain("published");
        contract.AllowedValues.Should().Contain("archived");
    }

    [Fact]
    public void AllowedValues_EmptyList_IsValid()
    {
        var contract = new FieldValidationContract(false, null, null, null, null, null, new List<string>());
        
        contract.AllowedValues.Should().BeEmpty();
    }
}
