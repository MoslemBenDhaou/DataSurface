using DataSurface.Core.Annotations;
using FluentAssertions;
using Xunit;

namespace DataSurface.Tests.Unit.Core;

public class CrudTenantAttributeTests
{
    [Fact]
    public void ClaimType_DefaultValue_IsTenantId()
    {
        var attr = new CrudTenantAttribute();
        
        attr.ClaimType.Should().Be("tenant_id");
    }

    [Fact]
    public void ClaimType_WhenSet_ReturnsSetValue()
    {
        var attr = new CrudTenantAttribute { ClaimType = "organization_id" };
        
        attr.ClaimType.Should().Be("organization_id");
    }

    [Fact]
    public void Required_DefaultValue_IsTrue()
    {
        var attr = new CrudTenantAttribute();
        
        attr.Required.Should().BeTrue();
    }

    [Fact]
    public void Required_WhenSetToFalse_ReturnsFalse()
    {
        var attr = new CrudTenantAttribute { Required = false };
        
        attr.Required.Should().BeFalse();
    }

    [Fact]
    public void AllProperties_CanBeSetTogether()
    {
        var attr = new CrudTenantAttribute
        {
            ClaimType = "company_id",
            Required = false
        };
        
        attr.ClaimType.Should().Be("company_id");
        attr.Required.Should().BeFalse();
    }
}
