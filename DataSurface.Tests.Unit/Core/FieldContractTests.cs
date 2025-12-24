using DataSurface.Core.Contracts;
using DataSurface.Core.Enums;
using FluentAssertions;
using Xunit;

namespace DataSurface.Tests.Unit.Core;

public class FieldContractTests
{
    [Fact]
    public void Constructor_WithAllParameters_SetsAllProperties()
    {
        var validation = new FieldValidationContract(true, 1, 100, 0, 1000, @"^\w+$", new[] { "a", "b" });
        
        var contract = new FieldContract(
            Name: "Title",
            ApiName: "title",
            Type: FieldType.String,
            Nullable: false,
            InRead: true,
            InCreate: true,
            InUpdate: true,
            Filterable: true,
            Sortable: true,
            Hidden: false,
            Immutable: false,
            Searchable: true,
            Computed: false,
            ComputedExpression: null,
            DefaultValue: "untitled",
            Validation: validation
        );
        
        contract.Name.Should().Be("Title");
        contract.ApiName.Should().Be("title");
        contract.Type.Should().Be(FieldType.String);
        contract.Nullable.Should().BeFalse();
        contract.InRead.Should().BeTrue();
        contract.InCreate.Should().BeTrue();
        contract.InUpdate.Should().BeTrue();
        contract.Filterable.Should().BeTrue();
        contract.Sortable.Should().BeTrue();
        contract.Hidden.Should().BeFalse();
        contract.Immutable.Should().BeFalse();
        contract.Searchable.Should().BeTrue();
        contract.Computed.Should().BeFalse();
        contract.ComputedExpression.Should().BeNull();
        contract.DefaultValue.Should().Be("untitled");
        contract.Validation.Should().Be(validation);
    }

    [Fact]
    public void ComputedField_SetsComputedAndExpression()
    {
        var validation = new FieldValidationContract(false, null, null, null, null, null);
        
        var contract = new FieldContract(
            Name: "FullName",
            ApiName: "fullName",
            Type: FieldType.String,
            Nullable: true,
            InRead: true,
            InCreate: false,
            InUpdate: false,
            Filterable: false,
            Sortable: false,
            Hidden: false,
            Immutable: true,
            Searchable: false,
            Computed: true,
            ComputedExpression: "FirstName + ' ' + LastName",
            DefaultValue: null,
            Validation: validation
        );
        
        contract.Computed.Should().BeTrue();
        contract.ComputedExpression.Should().Be("FirstName + ' ' + LastName");
        contract.InCreate.Should().BeFalse();
        contract.InUpdate.Should().BeFalse();
        contract.Immutable.Should().BeTrue();
    }

    [Fact]
    public void SearchableField_SetsSearchableTrue()
    {
        var validation = new FieldValidationContract(false, null, null, null, null, null);
        
        var contract = new FieldContract(
            Name: "Description",
            ApiName: "description",
            Type: FieldType.String,
            Nullable: true,
            InRead: true,
            InCreate: true,
            InUpdate: true,
            Filterable: true,
            Sortable: false,
            Hidden: false,
            Immutable: false,
            Searchable: true,
            Computed: false,
            ComputedExpression: null,
            DefaultValue: null,
            Validation: validation
        );
        
        contract.Searchable.Should().BeTrue();
    }

    [Fact]
    public void DefaultValue_CanBeVariousTypes()
    {
        var validation = new FieldValidationContract(false, null, null, null, null, null);
        
        // String default
        var stringContract = new FieldContract("Status", "status", FieldType.String, false,
            true, true, true, true, false, false, false, false, false, null, "active", validation);
        stringContract.DefaultValue.Should().Be("active");
        
        // Int default
        var intContract = new FieldContract("Priority", "priority", FieldType.Int32, false,
            true, true, true, true, false, false, false, false, false, null, 0, validation);
        intContract.DefaultValue.Should().Be(0);
        
        // Bool default
        var boolContract = new FieldContract("IsActive", "isActive", FieldType.Boolean, false,
            true, true, true, true, false, false, false, false, false, null, true, validation);
        boolContract.DefaultValue.Should().Be(true);
    }
}
