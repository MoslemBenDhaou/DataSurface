using DataSurface.Core.Annotations;
using DataSurface.Core.Enums;
using FluentAssertions;
using Xunit;

namespace DataSurface.Tests.Unit.Core;

public class CrudFieldAttributeTests
{
    [Fact]
    public void Constructor_WithCrudDto_SetsInProperty()
    {
        var attr = new CrudFieldAttribute(CrudDto.Read | CrudDto.Create);
        
        attr.In.Should().Be(CrudDto.Read | CrudDto.Create);
    }

    [Fact]
    public void Searchable_DefaultValue_IsFalse()
    {
        var attr = new CrudFieldAttribute(CrudDto.Read);
        
        attr.Searchable.Should().BeFalse();
    }

    [Fact]
    public void Searchable_WhenSet_ReturnsSetValue()
    {
        var attr = new CrudFieldAttribute(CrudDto.Read) { Searchable = true };
        
        attr.Searchable.Should().BeTrue();
    }

    [Fact]
    public void DefaultValue_DefaultValue_IsNull()
    {
        var attr = new CrudFieldAttribute(CrudDto.Read);
        
        attr.DefaultValue.Should().BeNull();
    }

    [Fact]
    public void DefaultValue_WhenSetToString_ReturnsSetValue()
    {
        var attr = new CrudFieldAttribute(CrudDto.Read) { DefaultValue = "pending" };
        
        attr.DefaultValue.Should().Be("pending");
    }

    [Fact]
    public void DefaultValue_WhenSetToInt_ReturnsSetValue()
    {
        var attr = new CrudFieldAttribute(CrudDto.Read) { DefaultValue = 42 };
        
        attr.DefaultValue.Should().Be(42);
    }

    [Fact]
    public void ComputedExpression_DefaultValue_IsNull()
    {
        var attr = new CrudFieldAttribute(CrudDto.Read);
        
        attr.ComputedExpression.Should().BeNull();
    }

    [Fact]
    public void ComputedExpression_WhenSet_ReturnsSetValue()
    {
        var attr = new CrudFieldAttribute(CrudDto.Read) { ComputedExpression = "FirstName + ' ' + LastName" };
        
        attr.ComputedExpression.Should().Be("FirstName + ' ' + LastName");
    }

    [Fact]
    public void AllowedValues_DefaultValue_IsNull()
    {
        var attr = new CrudFieldAttribute(CrudDto.Read);
        
        attr.AllowedValues.Should().BeNull();
    }

    [Fact]
    public void AllowedValues_WhenSet_ReturnsSetValue()
    {
        var attr = new CrudFieldAttribute(CrudDto.Read) { AllowedValues = "Active|Inactive|Pending" };
        
        attr.AllowedValues.Should().Be("Active|Inactive|Pending");
    }

    [Fact]
    public void ApiName_WhenSet_ReturnsSetValue()
    {
        var attr = new CrudFieldAttribute(CrudDto.Read) { ApiName = "customName" };
        
        attr.ApiName.Should().Be("customName");
    }

    [Fact]
    public void RequiredOnCreate_WhenSet_ReturnsSetValue()
    {
        var attr = new CrudFieldAttribute(CrudDto.Create) { RequiredOnCreate = true };
        
        attr.RequiredOnCreate.Should().BeTrue();
    }

    [Fact]
    public void Immutable_WhenSet_ReturnsSetValue()
    {
        var attr = new CrudFieldAttribute(CrudDto.Read) { Immutable = true };
        
        attr.Immutable.Should().BeTrue();
    }

    [Fact]
    public void Hidden_WhenSet_ReturnsSetValue()
    {
        var attr = new CrudFieldAttribute(CrudDto.Read) { Hidden = true };
        
        attr.Hidden.Should().BeTrue();
    }

    [Fact]
    public void ValidationProperties_WhenSet_ReturnSetValues()
    {
        var attr = new CrudFieldAttribute(CrudDto.Read)
        {
            MinLength = 1,
            MaxLength = 100,
            Min = 0,
            Max = 1000,
            Regex = @"^\d+$"
        };
        
        attr.MinLength.Should().Be(1);
        attr.MaxLength.Should().Be(100);
        attr.Min.Should().Be(0);
        attr.Max.Should().Be(1000);
        attr.Regex.Should().Be(@"^\d+$");
    }

    [Fact]
    public void AllProperties_CanBeSetTogether()
    {
        var attr = new CrudFieldAttribute(CrudDto.Read | CrudDto.Create | CrudDto.Filter)
        {
            ApiName = "status",
            RequiredOnCreate = true,
            Immutable = false,
            Hidden = false,
            MinLength = 1,
            MaxLength = 20,
            Searchable = true,
            DefaultValue = "active",
            AllowedValues = "active|inactive|pending"
        };
        
        attr.In.Should().Be(CrudDto.Read | CrudDto.Create | CrudDto.Filter);
        attr.ApiName.Should().Be("status");
        attr.RequiredOnCreate.Should().BeTrue();
        attr.Searchable.Should().BeTrue();
        attr.DefaultValue.Should().Be("active");
        attr.AllowedValues.Should().Be("active|inactive|pending");
    }
}
