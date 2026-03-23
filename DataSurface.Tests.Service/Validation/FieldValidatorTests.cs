using System.Text.Json.Nodes;
using DataSurface.Core.Contracts;
using DataSurface.Core.Enums;
using DataSurface.EFCore.Validation;
using DataSurface.Tests.Service.Shared.Builders;
using FluentAssertions;
using Xunit;

namespace DataSurface.Tests.Service.Validation;

/// <summary>
/// Unit tests for <see cref="FieldValidator"/> covering all constraint types.
/// </summary>
public class FieldValidatorTests
{
    private static ResourceContract BuildContract(params FieldContract[] fields)
    {
        var builder = new ResourceContractBuilder("Test", "tests")
            .Key("Id", FieldType.Int32)
            .WithField(new FieldBuilder("Id").OfType(FieldType.Int32).InRead().Build());
        foreach (var f in fields)
            builder.WithField(f);
        return builder.EnableAllOperations().Build();
    }

    // ────────────────────────────────────────────
    //  MinLength
    // ────────────────────────────────────────────

    [Fact]
    public void MinLength_BelowThreshold_ReportsError()
    {
        var contract = BuildContract(new FieldBuilder("Name").OfType(FieldType.String).InCreate().MinLength(3).Build());
        var body = new JsonObject { ["name"] = "AB" };
        var errors = new Dictionary<string, string[]>();

        FieldValidator.ValidateFieldConstraints(contract, body, errors);

        errors.Should().ContainKey("name");
        errors["name"].Should().Contain(e => e.Contains("Minimum length"));
    }

    [Fact]
    public void MinLength_ExactThreshold_NoError()
    {
        var contract = BuildContract(new FieldBuilder("Name").OfType(FieldType.String).InCreate().MinLength(3).Build());
        var body = new JsonObject { ["name"] = "ABC" };
        var errors = new Dictionary<string, string[]>();

        FieldValidator.ValidateFieldConstraints(contract, body, errors);

        errors.Should().NotContainKey("name");
    }

    // ────────────────────────────────────────────
    //  MaxLength
    // ────────────────────────────────────────────

    [Fact]
    public void MaxLength_AboveThreshold_ReportsError()
    {
        var contract = BuildContract(new FieldBuilder("Name").OfType(FieldType.String).InCreate().MaxLength(5).Build());
        var body = new JsonObject { ["name"] = "ABCDEF" };
        var errors = new Dictionary<string, string[]>();

        FieldValidator.ValidateFieldConstraints(contract, body, errors);

        errors.Should().ContainKey("name");
        errors["name"].Should().Contain(e => e.Contains("Maximum length"));
    }

    [Fact]
    public void MaxLength_ExactThreshold_NoError()
    {
        var contract = BuildContract(new FieldBuilder("Name").OfType(FieldType.String).InCreate().MaxLength(5).Build());
        var body = new JsonObject { ["name"] = "ABCDE" };
        var errors = new Dictionary<string, string[]>();

        FieldValidator.ValidateFieldConstraints(contract, body, errors);

        errors.Should().NotContainKey("name");
    }

    // ────────────────────────────────────────────
    //  Min (numeric)
    // ────────────────────────────────────────────

    [Fact]
    public void Min_BelowThreshold_ReportsError()
    {
        var contract = BuildContract(new FieldBuilder("Price").OfType(FieldType.Decimal).InCreate().Min(1m).Build());
        var body = new JsonObject { ["price"] = 0.5m };
        var errors = new Dictionary<string, string[]>();

        FieldValidator.ValidateFieldConstraints(contract, body, errors);

        errors.Should().ContainKey("price");
        errors["price"].Should().Contain(e => e.Contains("Minimum value"));
    }

    [Fact]
    public void Min_ExactThreshold_NoError()
    {
        var contract = BuildContract(new FieldBuilder("Price").OfType(FieldType.Decimal).InCreate().Min(1m).Build());
        var body = new JsonObject { ["price"] = 1m };
        var errors = new Dictionary<string, string[]>();

        FieldValidator.ValidateFieldConstraints(contract, body, errors);

        errors.Should().NotContainKey("price");
    }

    // ────────────────────────────────────────────
    //  Max (numeric)
    // ────────────────────────────────────────────

    [Fact]
    public void Max_AboveThreshold_ReportsError()
    {
        var contract = BuildContract(new FieldBuilder("Price").OfType(FieldType.Decimal).InCreate().Max(100m).Build());
        var body = new JsonObject { ["price"] = 101m };
        var errors = new Dictionary<string, string[]>();

        FieldValidator.ValidateFieldConstraints(contract, body, errors);

        errors.Should().ContainKey("price");
        errors["price"].Should().Contain(e => e.Contains("Maximum value"));
    }

    [Fact]
    public void Max_ExactThreshold_NoError()
    {
        var contract = BuildContract(new FieldBuilder("Price").OfType(FieldType.Decimal).InCreate().Max(100m).Build());
        var body = new JsonObject { ["price"] = 100m };
        var errors = new Dictionary<string, string[]>();

        FieldValidator.ValidateFieldConstraints(contract, body, errors);

        errors.Should().NotContainKey("price");
    }

    // ────────────────────────────────────────────
    //  Regex
    // ────────────────────────────────────────────

    [Fact]
    public void Regex_NonMatching_ReportsError()
    {
        var contract = BuildContract(new FieldBuilder("Code").OfType(FieldType.String).InCreate().Regex(@"^[A-Z]{3}$").Build());
        var body = new JsonObject { ["code"] = "ab" };
        var errors = new Dictionary<string, string[]>();

        FieldValidator.ValidateFieldConstraints(contract, body, errors);

        errors.Should().ContainKey("code");
        errors["code"].Should().Contain(e => e.Contains("pattern"));
    }

    [Fact]
    public void Regex_Matching_NoError()
    {
        var contract = BuildContract(new FieldBuilder("Code").OfType(FieldType.String).InCreate().Regex(@"^[A-Z]{3}$").Build());
        var body = new JsonObject { ["code"] = "ABC" };
        var errors = new Dictionary<string, string[]>();

        FieldValidator.ValidateFieldConstraints(contract, body, errors);

        errors.Should().NotContainKey("code");
    }

    // ────────────────────────────────────────────
    //  AllowedValues
    // ────────────────────────────────────────────

    [Fact]
    public void AllowedValues_InvalidValue_ReportsError()
    {
        var contract = BuildContract(new FieldBuilder("Status").OfType(FieldType.String).InCreate()
            .AllowedValues("active", "inactive").Build());
        var body = new JsonObject { ["status"] = "deleted" };
        var errors = new Dictionary<string, string[]>();

        FieldValidator.ValidateFieldConstraints(contract, body, errors);

        errors.Should().ContainKey("status");
        errors["status"].Should().Contain(e => e.Contains("must be one of"));
    }

    [Fact]
    public void AllowedValues_ValidValue_NoError()
    {
        var contract = BuildContract(new FieldBuilder("Status").OfType(FieldType.String).InCreate()
            .AllowedValues("active", "inactive").Build());
        var body = new JsonObject { ["status"] = "active" };
        var errors = new Dictionary<string, string[]>();

        FieldValidator.ValidateFieldConstraints(contract, body, errors);

        errors.Should().NotContainKey("status");
    }

    [Fact]
    public void AllowedValues_CaseInsensitive_NoError()
    {
        var contract = BuildContract(new FieldBuilder("Status").OfType(FieldType.String).InCreate()
            .AllowedValues("active", "inactive").Build());
        var body = new JsonObject { ["status"] = "ACTIVE" };
        var errors = new Dictionary<string, string[]>();

        FieldValidator.ValidateFieldConstraints(contract, body, errors);

        errors.Should().NotContainKey("status");
    }

    // ────────────────────────────────────────────
    //  Multiple constraints on one field
    // ────────────────────────────────────────────

    [Fact]
    public void MultipleConstraints_AllViolated_ReportsMultipleErrors()
    {
        var contract = BuildContract(new FieldBuilder("Name").OfType(FieldType.String).InCreate()
            .MinLength(5).MaxLength(10).Regex(@"^[A-Z]+$").Build());
        var body = new JsonObject { ["name"] = "ab" }; // too short + wrong case

        var errors = new Dictionary<string, string[]>();
        FieldValidator.ValidateFieldConstraints(contract, body, errors);

        errors.Should().ContainKey("name");
        errors["name"].Should().HaveCountGreaterOrEqualTo(2);
    }

    // ────────────────────────────────────────────
    //  IsNumericFieldType
    // ────────────────────────────────────────────

    [Theory]
    [InlineData(FieldType.Int32, true)]
    [InlineData(FieldType.Int64, true)]
    [InlineData(FieldType.Decimal, true)]
    [InlineData(FieldType.String, false)]
    [InlineData(FieldType.Boolean, false)]
    [InlineData(FieldType.DateTime, false)]
    [InlineData(FieldType.Guid, false)]
    public void IsNumericFieldType_ReturnsExpected(FieldType type, bool expected)
    {
        FieldValidator.IsNumericFieldType(type).Should().Be(expected);
    }

    // ────────────────────────────────────────────
    //  Null node skipped
    // ────────────────────────────────────────────

    [Fact]
    public void NullJsonValue_SkipsValidation()
    {
        var contract = BuildContract(new FieldBuilder("Name").OfType(FieldType.String).InCreate().MinLength(5).Build());
        var body = new JsonObject { ["name"] = null };
        var errors = new Dictionary<string, string[]>();

        FieldValidator.ValidateFieldConstraints(contract, body, errors);

        errors.Should().NotContainKey("name");
    }

    // ────────────────────────────────────────────
    //  Unknown field ignored
    // ────────────────────────────────────────────

    [Fact]
    public void UnknownField_NotInContract_Ignored()
    {
        var contract = BuildContract(new FieldBuilder("Name").OfType(FieldType.String).InCreate().Build());
        var body = new JsonObject { ["bogus"] = "value" };
        var errors = new Dictionary<string, string[]>();

        FieldValidator.ValidateFieldConstraints(contract, body, errors);

        errors.Should().BeEmpty();
    }
}
