using DataSurface.Core;
using DataSurface.Core.ContractBuilderModels;
using DataSurface.Core.Enums;
using DataSurface.Tests.Service.Shared.Entities;
using FluentAssertions;
using Xunit;

namespace DataSurface.Tests.Service.ContractBuilder;

/// <summary>
/// Behavioral tests for <see cref="Core.ContractBuilder"/>:
/// verifies contract generation from annotated CLR types produces the expected resource contracts.
/// </summary>
public class ContractBuilderBehaviorTests
{
    private readonly Core.ContractBuilder _builder = new();

    // ────────────────────────────────────────────
    //  Route & Resource Key
    // ────────────────────────────────────────────

    [Fact]
    public void Build_SetsRouteFromAttribute()
    {
        var contracts = _builder.BuildFromTypes(typeof(ProductEntity));

        contracts.Should().ContainSingle()
            .Which.Route.Should().Be("products");
    }

    [Fact]
    public void Build_SetsResourceKeyToTypeNameWhenNotExplicit()
    {
        var contracts = _builder.BuildFromTypes(typeof(ProductEntity));

        contracts.Single().ResourceKey.Should().Be("ProductEntity");
    }

    [Fact]
    public void Build_SetsExplicitResourceKeyWhenProvided()
    {
        var contracts = _builder.BuildFromTypes(typeof(CustomKeyEntity));

        contracts.Single().ResourceKey.Should().Be("CustomItem");
    }

    // ────────────────────────────────────────────
    //  Key Discovery
    // ────────────────────────────────────────────

    [Fact]
    public void Build_DiscoversKeyMarkedWithCrudKeyAttribute()
    {
        var contracts = _builder.BuildFromTypes(typeof(ProductEntity));
        var contract = contracts.Single();

        contract.Key.Name.Should().Be("Id");
        contract.Key.Type.Should().Be(FieldType.Int32);
    }

    [Fact]
    public void Build_DiscoversGuidKey()
    {
        var contracts = _builder.BuildFromTypes(typeof(OrderEntity));
        var contract = contracts.Single();

        contract.Key.Name.Should().Be("Id");
        contract.Key.Type.Should().Be(FieldType.Guid);
    }

    [Fact]
    public void Build_DiscoversStringKey()
    {
        var contracts = _builder.BuildFromTypes(typeof(StringKeyEntity));
        var contract = contracts.Single();

        contract.Key.Name.Should().Be("Slug");
        contract.Key.Type.Should().Be(FieldType.String);
    }

    [Fact]
    public void Build_FallsBackToIdPropertyWhenNoCrudKeyAttribute()
    {
        var contracts = _builder.BuildFromTypes(typeof(NoExplicitKeyEntity));
        var contract = contracts.Single();

        contract.Key.Name.Should().Be("Id");
        contract.Key.Type.Should().Be(FieldType.Int32);
    }

    // ────────────────────────────────────────────
    //  Field Exposure (Opt-In)
    // ────────────────────────────────────────────

    [Fact]
    public void Build_ExposesOnlyAnnotatedFieldsByDefault()
    {
        var contracts = _builder.BuildFromTypes(typeof(MinimalEntity));
        var contract = contracts.Single();

        contract.Fields.Select(f => f.Name)
            .Should().BeEquivalentTo(new[] { "Id", "Value" });
    }

    [Fact]
    public void Build_ExposesNoFieldsForBareEntityWithOptInEnabled()
    {
        var builder = new Core.ContractBuilder(new ContractBuilderOptions { ExposeFieldsOnlyWhenAnnotated = true });
        var contracts = builder.BuildFromTypes(typeof(BareEntity));
        var contract = contracts.Single();

        // Only the auto-injected key field
        contract.Fields.Should().ContainSingle()
            .Which.Name.Should().Be("Id");
    }

    [Fact]
    public void Build_ExposesAllScalarsWhenOptInDisabled()
    {
        var builder = new Core.ContractBuilder(new ContractBuilderOptions
        {
            ExposeFieldsOnlyWhenAnnotated = false,
            DefaultIncludeScalarsInRead = true
        });
        var contracts = builder.BuildFromTypes(typeof(BareEntity));
        var contract = contracts.Single();

        contract.Fields.Should().Contain(f => f.Name == "Name");
    }

    // ────────────────────────────────────────────
    //  DTO Membership (Read / Create / Update)
    // ────────────────────────────────────────────

    [Fact]
    public void Build_SetsInReadCreateUpdateFromCrudDtoFlags()
    {
        var contracts = _builder.BuildFromTypes(typeof(ProductEntity));
        var contract = contracts.Single();

        var nameField = contract.Fields.First(f => f.Name == "Name");
        nameField.InRead.Should().BeTrue();
        nameField.InCreate.Should().BeTrue();
        nameField.InUpdate.Should().BeTrue();
    }

    [Fact]
    public void Build_ReadOnlyFieldExcludesCreateAndUpdate()
    {
        var contracts = _builder.BuildFromTypes(typeof(ProductEntity));
        var contract = contracts.Single();

        var createdAt = contract.Fields.First(f => f.Name == "CreatedAt");
        createdAt.InRead.Should().BeTrue();
        createdAt.InCreate.Should().BeFalse();
        createdAt.InUpdate.Should().BeFalse();
    }

    // ────────────────────────────────────────────
    //  Filterable / Sortable / Searchable
    // ────────────────────────────────────────────

    [Fact]
    public void Build_MarksFilterableFieldsInQueryContract()
    {
        var contracts = _builder.BuildFromTypes(typeof(ProductEntity));
        var contract = contracts.Single();

        contract.Query.FilterableFields.Should().Contain("name");
        contract.Query.FilterableFields.Should().Contain("price");
        contract.Query.FilterableFields.Should().Contain("status");
    }

    [Fact]
    public void Build_MarksSortableFieldsInQueryContract()
    {
        var contracts = _builder.BuildFromTypes(typeof(ProductEntity));
        var contract = contracts.Single();

        contract.Query.SortableFields.Should().Contain("name");
        contract.Query.SortableFields.Should().Contain("price");
        contract.Query.SortableFields.Should().Contain("createdAt");
    }

    [Fact]
    public void Build_MarksSearchableFieldsInQueryContract()
    {
        var contracts = _builder.BuildFromTypes(typeof(ProductEntity));
        var contract = contracts.Single();

        contract.Query.SearchableFields.Should().Contain("name");
        contract.Query.SearchableFields.Should().Contain("description");
    }

    // ────────────────────────────────────────────
    //  MaxPageSize
    // ────────────────────────────────────────────

    [Fact]
    public void Build_SetsMaxPageSizeFromAttribute()
    {
        var contracts = _builder.BuildFromTypes(typeof(ProductEntity));

        contracts.Single().Query.MaxPageSize.Should().Be(50);
    }

    [Fact]
    public void Build_DefaultsMaxPageSizeTo200()
    {
        var contracts = _builder.BuildFromTypes(typeof(MinimalEntity));

        contracts.Single().Query.MaxPageSize.Should().Be(200);
    }

    // ────────────────────────────────────────────
    //  Operation Enable/Disable
    // ────────────────────────────────────────────

    [Fact]
    public void Build_DisablesOperationsPerAttribute()
    {
        var contracts = _builder.BuildFromTypes(typeof(ProductEntity));
        var contract = contracts.Single();

        contract.Operations[CrudOperation.Delete].Enabled.Should().BeFalse();
        contract.Operations[CrudOperation.List].Enabled.Should().BeTrue();
        contract.Operations[CrudOperation.Get].Enabled.Should().BeTrue();
        contract.Operations[CrudOperation.Create].Enabled.Should().BeTrue();
        contract.Operations[CrudOperation.Update].Enabled.Should().BeTrue();
    }

    [Fact]
    public void Build_ReadOnlyEntityDisablesCreateUpdateDelete()
    {
        var contracts = _builder.BuildFromTypes(typeof(ReadOnlyEntity));
        var contract = contracts.Single();

        contract.Operations[CrudOperation.Create].Enabled.Should().BeFalse();
        contract.Operations[CrudOperation.Update].Enabled.Should().BeFalse();
        contract.Operations[CrudOperation.Delete].Enabled.Should().BeFalse();
        contract.Operations[CrudOperation.List].Enabled.Should().BeTrue();
        contract.Operations[CrudOperation.Get].Enabled.Should().BeTrue();
    }

    // ────────────────────────────────────────────
    //  Immutable Fields
    // ────────────────────────────────────────────

    [Fact]
    public void Build_ImmutableFieldIsExcludedFromUpdateShape()
    {
        var contracts = _builder.BuildFromTypes(typeof(ProductEntity));
        var contract = contracts.Single();

        var skuField = contract.Fields.First(f => f.Name == "Sku");
        skuField.Immutable.Should().BeTrue();
        skuField.InUpdate.Should().BeFalse();
        skuField.InCreate.Should().BeTrue();
    }

    [Fact]
    public void Build_KeyFieldIsAlwaysImmutable()
    {
        var contracts = _builder.BuildFromTypes(typeof(ProductEntity));
        var contract = contracts.Single();

        var keyField = contract.Fields.First(f => f.Name == "Id");
        keyField.Immutable.Should().BeTrue();
    }

    // ────────────────────────────────────────────
    //  Hidden & Ignored
    // ────────────────────────────────────────────

    [Fact]
    public void Build_HiddenFieldNotExposedInAnyShape()
    {
        var contracts = _builder.BuildFromTypes(typeof(ProductEntity));
        var contract = contracts.Single();

        contract.Fields.Should().NotContain(f => f.Name == "InternalNotes");
    }

    [Fact]
    public void Build_IgnoredFieldNotIncludedInContract()
    {
        var contracts = _builder.BuildFromTypes(typeof(ProductEntity));
        var contract = contracts.Single();

        contract.Fields.Should().NotContain(f => f.Name == "TransientData");
    }

    // ────────────────────────────────────────────
    //  Default Values
    // ────────────────────────────────────────────

    [Fact]
    public void Build_SetsDefaultValueFromAttribute()
    {
        var contracts = _builder.BuildFromTypes(typeof(ProductEntity));
        var contract = contracts.Single();

        var statusField = contract.Fields.First(f => f.Name == "Status");
        statusField.DefaultValue.Should().Be("draft");
    }

    // ────────────────────────────────────────────
    //  Allowed Values (Validation)
    // ────────────────────────────────────────────

    [Fact]
    public void Build_ParsesAllowedValuesFromPipeSeparatedString()
    {
        var contracts = _builder.BuildFromTypes(typeof(ProductEntity));
        var contract = contracts.Single();

        var statusField = contract.Fields.First(f => f.Name == "Status");
        statusField.Validation.AllowedValues.Should().BeEquivalentTo(new[] { "active", "discontinued", "draft" });
    }

    // ────────────────────────────────────────────
    //  Computed Fields
    // ────────────────────────────────────────────

    [Fact]
    public void Build_ComputedFieldIsReadOnlyAndImmutable()
    {
        var contracts = _builder.BuildFromTypes(typeof(ProductEntity));
        var contract = contracts.Single();

        var displayName = contract.Fields.First(f => f.Name == "DisplayName");
        displayName.Computed.Should().BeTrue();
        displayName.ComputedExpression.Should().Be("Name + ' (' + Status + ')'");
        displayName.InCreate.Should().BeFalse();
        displayName.InUpdate.Should().BeFalse();
        displayName.Immutable.Should().BeTrue();
    }

    // ────────────────────────────────────────────
    //  RequiredOnCreate
    // ────────────────────────────────────────────

    [Fact]
    public void Build_RequiredOnCreateFieldAppearsInCreateOperationRequired()
    {
        var contracts = _builder.BuildFromTypes(typeof(ProductEntity));
        var contract = contracts.Single();

        contract.Operations[CrudOperation.Create].RequiredOnCreate
            .Should().Contain("name")
            .And.Contain("price");
    }

    // ────────────────────────────────────────────
    //  API Name Casing
    // ────────────────────────────────────────────

    [Fact]
    public void Build_GeneratesCamelCaseApiNamesByDefault()
    {
        var contracts = _builder.BuildFromTypes(typeof(ProductEntity));
        var contract = contracts.Single();

        contract.Fields.First(f => f.Name == "CreatedAt").ApiName.Should().Be("createdAt");
        contract.Fields.First(f => f.Name == "DisplayName").ApiName.Should().Be("displayName");
    }

    [Fact]
    public void Build_PreservesPascalCaseWhenConfigured()
    {
        var builder = new Core.ContractBuilder(new ContractBuilderOptions { UseCamelCaseApiNames = false });
        var contracts = builder.BuildFromTypes(typeof(ProductEntity));
        var contract = contracts.Single();

        contract.Fields.First(f => f.Name == "CreatedAt").ApiName.Should().Be("CreatedAt");
    }

    // ────────────────────────────────────────────
    //  Concurrency
    // ────────────────────────────────────────────

    [Fact]
    public void Build_ConcurrencyAttributePopulatesUpdateOperation()
    {
        var contracts = _builder.BuildFromTypes(typeof(OrderEntity));
        var contract = contracts.Single();

        var updateOp = contract.Operations[CrudOperation.Update];
        updateOp.Concurrency.Should().NotBeNull();
        updateOp.Concurrency!.Mode.Should().Be(ConcurrencyMode.RowVersion);
        updateOp.Concurrency.RequiredOnUpdate.Should().BeTrue();
    }

    [Fact]
    public void Build_ConcurrencyOnlyAppliedToUpdateOperation()
    {
        var contracts = _builder.BuildFromTypes(typeof(OrderEntity));
        var contract = contracts.Single();

        contract.Operations[CrudOperation.Create].Concurrency.Should().BeNull();
        contract.Operations[CrudOperation.List].Concurrency.Should().BeNull();
        contract.Operations[CrudOperation.Get].Concurrency.Should().BeNull();
        contract.Operations[CrudOperation.Delete].Concurrency.Should().BeNull();
    }

    // ────────────────────────────────────────────
    //  Tenant Isolation
    // ────────────────────────────────────────────

    [Fact]
    public void Build_TenantAttributePopulatesTenantContract()
    {
        var contracts = _builder.BuildFromTypes(typeof(OrderEntity));
        var contract = contracts.Single();

        contract.Tenant.Should().NotBeNull();
        contract.Tenant!.FieldName.Should().Be("TenantId");
        contract.Tenant.ClaimType.Should().Be("tenant_id");
        contract.Tenant.Required.Should().BeTrue();
    }

    [Fact]
    public void Build_NoTenantAttributeResultsInNullTenant()
    {
        var contracts = _builder.BuildFromTypes(typeof(MinimalEntity));
        var contract = contracts.Single();

        contract.Tenant.Should().BeNull();
    }

    // ────────────────────────────────────────────
    //  Security Policies
    // ────────────────────────────────────────────

    [Fact]
    public void Build_GlobalAuthorizePolicyAppliesToAllOperations()
    {
        var contracts = _builder.BuildFromTypes(typeof(OrderEntity));
        var contract = contracts.Single();

        // OrderEntity has [CrudAuthorize("OrderAdmin")] (global, no Operation set)
        // so it applies to all operations
        contract.Security.Policies[CrudOperation.List].Should().Be("OrderAdmin");
        contract.Security.Policies[CrudOperation.Get].Should().Be("OrderAdmin");
        contract.Security.Policies[CrudOperation.Create].Should().Be("OrderAdmin");
        contract.Security.Policies[CrudOperation.Update].Should().Be("OrderAdmin");
        contract.Security.Policies[CrudOperation.Delete].Should().Be("OrderAdmin");
    }

    [Fact]
    public void Build_NoAuthAttributeResultsInEmptyPolicies()
    {
        var contracts = _builder.BuildFromTypes(typeof(MinimalEntity));
        var contract = contracts.Single();

        contract.Security.Policies.Should().BeEmpty();
    }

    // ────────────────────────────────────────────
    //  Relations
    // ────────────────────────────────────────────

    [Fact]
    public void Build_RelationAttributeCreatesRelationContract()
    {
        var contracts = _builder.BuildFromTypes(typeof(CategoryEntity), typeof(ProductEntity));
        var categoryContract = contracts.First(c => c.ResourceKey == "CategoryEntity");

        categoryContract.Relations.Should().ContainSingle();
        var rel = categoryContract.Relations.Single();
        rel.Name.Should().Be("Products");
        rel.Kind.Should().Be(RelationKind.OneToMany);
        rel.TargetResourceKey.Should().Be("ProductEntity");
    }

    [Fact]
    public void Build_RelationWithExpandAllowedAppearsInReadContract()
    {
        var contracts = _builder.BuildFromTypes(typeof(CategoryEntity), typeof(ProductEntity));
        var categoryContract = contracts.First(c => c.ResourceKey == "CategoryEntity");

        categoryContract.Read.ExpandAllowed.Should().Contain("products");
    }

    // ────────────────────────────────────────────
    //  Operation Input/Output Shapes
    // ────────────────────────────────────────────

    [Fact]
    public void Build_CreateInputShapeMatchesFieldsWithInCreate()
    {
        var contracts = _builder.BuildFromTypes(typeof(ProductEntity));
        var contract = contracts.Single();

        var createOp = contract.Operations[CrudOperation.Create];
        var expectedCreateFields = contract.Fields
            .Where(f => f.InCreate)
            .Select(f => f.ApiName)
            .ToList();

        createOp.InputShape.Should().BeEquivalentTo(expectedCreateFields);
    }

    [Fact]
    public void Build_UpdateInputShapeExcludesImmutableFields()
    {
        var contracts = _builder.BuildFromTypes(typeof(ProductEntity));
        var contract = contracts.Single();

        var updateOp = contract.Operations[CrudOperation.Update];
        updateOp.InputShape.Should().NotContain("sku");
        updateOp.InputShape.Should().NotContain("id");
    }

    [Fact]
    public void Build_ListOutputShapeMatchesReadFields()
    {
        var contracts = _builder.BuildFromTypes(typeof(ProductEntity));
        var contract = contracts.Single();

        var listOp = contract.Operations[CrudOperation.List];
        var expectedReadFields = contract.Fields
            .Where(f => f.InRead)
            .Select(f => f.ApiName)
            .ToList();

        listOp.OutputShape.Should().BeEquivalentTo(expectedReadFields);
    }

    // ────────────────────────────────────────────
    //  Validation: Duplicate Routes
    // ────────────────────────────────────────────

    [Fact]
    public void Build_ThrowsOnDuplicateRoutes()
    {
        var act = () => _builder.BuildFromTypes(typeof(ProductEntity), typeof(DuplicateRouteEntity));

        act.Should().Throw<ContractValidationException>()
            .Where(e => e.Errors.Any(err => err.Contains("Duplicate route")));
    }

    // ────────────────────────────────────────────
    //  Multiple Types in Single Call
    // ────────────────────────────────────────────

    [Fact]
    public void Build_MultipleTypesProducesMultipleContracts()
    {
        var contracts = _builder.BuildFromTypes(typeof(MinimalEntity), typeof(ReadOnlyEntity));

        contracts.Should().HaveCount(2);
        contracts.Should().Contain(c => c.Route == "minimal-items");
        contracts.Should().Contain(c => c.Route == "readonly-items");
    }

    // ────────────────────────────────────────────
    //  Field Type Mapping
    // ────────────────────────────────────────────

    [Fact]
    public void Build_MapsIntPropertyToInt32FieldType()
    {
        var contracts = _builder.BuildFromTypes(typeof(ProductEntity));
        var contract = contracts.Single();

        contract.Fields.First(f => f.Name == "Id").Type.Should().Be(FieldType.Int32);
    }

    [Fact]
    public void Build_MapsDecimalPropertyToDecimalFieldType()
    {
        var contracts = _builder.BuildFromTypes(typeof(ProductEntity));
        var contract = contracts.Single();

        contract.Fields.First(f => f.Name == "Price").Type.Should().Be(FieldType.Decimal);
    }

    [Fact]
    public void Build_MapsDateTimePropertyToDateTimeFieldType()
    {
        var contracts = _builder.BuildFromTypes(typeof(ProductEntity));
        var contract = contracts.Single();

        contract.Fields.First(f => f.Name == "CreatedAt").Type.Should().Be(FieldType.DateTime);
    }

    [Fact]
    public void Build_MapsGuidPropertyToGuidFieldType()
    {
        var contracts = _builder.BuildFromTypes(typeof(OrderEntity));
        var contract = contracts.Single();

        contract.Fields.First(f => f.Name == "Id").Type.Should().Be(FieldType.Guid);
    }

    [Fact]
    public void Build_MapsNullableStringAsNullable()
    {
        var contracts = _builder.BuildFromTypes(typeof(ProductEntity));
        var contract = contracts.Single();

        contract.Fields.First(f => f.Name == "Description").Nullable.Should().BeTrue();
    }
}
