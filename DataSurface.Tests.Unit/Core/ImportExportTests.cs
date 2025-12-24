using System.Text.Json.Nodes;
using DataSurface.Core.ImportExport;
using FluentAssertions;
using Xunit;

namespace DataSurface.Tests.Unit.Core;

public class ImportOptionsTests
{
    [Fact]
    public void DefaultValues_AreCorrect()
    {
        var options = new ImportOptions();

        options.Format.Should().Be(ExportFormat.Json);
        options.UpsertMode.Should().BeFalse();
        options.SkipErrors.Should().BeFalse();
        options.BatchSize.Should().Be(100);
    }

    [Fact]
    public void AllProperties_CanBeSet()
    {
        var options = new ImportOptions
        {
            Format = ExportFormat.Csv,
            UpsertMode = true,
            SkipErrors = true,
            BatchSize = 500
        };

        options.Format.Should().Be(ExportFormat.Csv);
        options.UpsertMode.Should().BeTrue();
        options.SkipErrors.Should().BeTrue();
        options.BatchSize.Should().Be(500);
    }
}

public class ExportResultTests
{
    [Fact]
    public void Constructor_WithAllParameters_SetsAllProperties()
    {
        using var stream = new MemoryStream();
        
        var result = new ExportResult(
            Data: stream,
            ContentType: "application/json",
            FileName: "users_export.json",
            RecordCount: 100
        );

        result.Data.Should().BeSameAs(stream);
        result.ContentType.Should().Be("application/json");
        result.FileName.Should().Be("users_export.json");
        result.RecordCount.Should().Be(100);
    }

    [Fact]
    public void CsvExport_HasCorrectContentType()
    {
        using var stream = new MemoryStream();
        
        var result = new ExportResult(
            Data: stream,
            ContentType: "text/csv",
            FileName: "users_export.csv",
            RecordCount: 50
        );

        result.ContentType.Should().Be("text/csv");
        result.FileName.Should().EndWith(".csv");
    }
}

public class ImportResultTests
{
    [Fact]
    public void Constructor_WithAllParameters_SetsAllProperties()
    {
        var errors = new List<ImportError>
        {
            new(1, "email", "Invalid email format", null),
            new(5, null, "Duplicate record", new JsonObject { ["id"] = 5 })
        };

        var result = new ImportResult(
            TotalRecords: 100,
            SuccessCount: 98,
            FailureCount: 2,
            Errors: errors
        );

        result.TotalRecords.Should().Be(100);
        result.SuccessCount.Should().Be(98);
        result.FailureCount.Should().Be(2);
        result.Errors.Should().HaveCount(2);
    }

    [Fact]
    public void SuccessfulImport_HasNoErrors()
    {
        var result = new ImportResult(
            TotalRecords: 50,
            SuccessCount: 50,
            FailureCount: 0,
            Errors: Array.Empty<ImportError>()
        );

        result.SuccessCount.Should().Be(result.TotalRecords);
        result.FailureCount.Should().Be(0);
        result.Errors.Should().BeEmpty();
    }
}

public class ImportErrorTests
{
    [Fact]
    public void Constructor_WithAllParameters_SetsAllProperties()
    {
        var data = new JsonObject { ["id"] = 1, ["name"] = "Test" };

        var error = new ImportError(
            RowNumber: 5,
            Field: "email",
            Message: "Invalid email format",
            Data: data
        );

        error.RowNumber.Should().Be(5);
        error.Field.Should().Be("email");
        error.Message.Should().Be("Invalid email format");
        error.Data.Should().NotBeNull();
    }

    [Fact]
    public void Constructor_WithNullOptionalFields_IsValid()
    {
        var error = new ImportError(
            RowNumber: 10,
            Field: null,
            Message: "General error",
            Data: null
        );

        error.RowNumber.Should().Be(10);
        error.Field.Should().BeNull();
        error.Data.Should().BeNull();
    }
}

public class ExportFormatTests
{
    [Fact]
    public void Json_HasCorrectValue()
    {
        ExportFormat.Json.Should().Be(ExportFormat.Json);
    }

    [Fact]
    public void Csv_HasCorrectValue()
    {
        ExportFormat.Csv.Should().Be(ExportFormat.Csv);
    }

    [Fact]
    public void AllFormats_AreDefined()
    {
        var formats = Enum.GetValues<ExportFormat>();
        
        formats.Should().Contain(ExportFormat.Json);
        formats.Should().Contain(ExportFormat.Csv);
        formats.Should().HaveCount(2);
    }
}
