using System.Text.Json.Nodes;

namespace DataSurface.Core.ImportExport;

/// <summary>
/// Interface for importing and exporting resource data.
/// </summary>
public interface IDataSurfaceImportExportService
{
    /// <summary>
    /// Exports resources to JSON format.
    /// </summary>
    /// <param name="resourceKey">The resource key to export.</param>
    /// <param name="format">Export format (json, csv).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Export result with data stream.</returns>
    Task<ExportResult> ExportAsync(string resourceKey, ExportFormat format, CancellationToken ct = default);

    /// <summary>
    /// Imports resources from a data stream.
    /// </summary>
    /// <param name="resourceKey">The resource key to import into.</param>
    /// <param name="data">The data stream to import.</param>
    /// <param name="options">Import options.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Import result with success/failure counts.</returns>
    Task<ImportResult> ImportAsync(string resourceKey, Stream data, ImportOptions options, CancellationToken ct = default);
}

/// <summary>
/// Export format options.
/// </summary>
public enum ExportFormat
{
    /// <summary>JSON array format.</summary>
    Json,
    /// <summary>CSV format with headers.</summary>
    Csv
}

/// <summary>
/// Result of an export operation.
/// </summary>
/// <param name="Data">The exported data stream.</param>
/// <param name="ContentType">MIME content type.</param>
/// <param name="FileName">Suggested file name.</param>
/// <param name="RecordCount">Number of records exported.</param>
public sealed record ExportResult(
    Stream Data,
    string ContentType,
    string FileName,
    int RecordCount
);

/// <summary>
/// Options for import operations.
/// </summary>
public sealed class ImportOptions
{
    /// <summary>
    /// Gets or sets the import format.
    /// </summary>
    public ExportFormat Format { get; set; } = ExportFormat.Json;

    /// <summary>
    /// Gets or sets whether to update existing records (upsert) or skip them.
    /// </summary>
    public bool UpsertMode { get; set; } = false;

    /// <summary>
    /// Gets or sets whether to skip validation errors and continue importing.
    /// </summary>
    public bool SkipErrors { get; set; } = false;

    /// <summary>
    /// Gets or sets the batch size for import operations.
    /// </summary>
    public int BatchSize { get; set; } = 100;
}

/// <summary>
/// Result of an import operation.
/// </summary>
/// <param name="TotalRecords">Total records in the import file.</param>
/// <param name="SuccessCount">Number of successfully imported records.</param>
/// <param name="FailureCount">Number of failed records.</param>
/// <param name="Errors">List of errors encountered during import.</param>
public sealed record ImportResult(
    int TotalRecords,
    int SuccessCount,
    int FailureCount,
    IReadOnlyList<ImportError> Errors
);

/// <summary>
/// Represents an error during import.
/// </summary>
/// <param name="RowNumber">The row number where the error occurred.</param>
/// <param name="Field">The field that caused the error (if applicable).</param>
/// <param name="Message">Error message.</param>
/// <param name="Data">The problematic data (if available).</param>
public sealed record ImportError(
    int RowNumber,
    string? Field,
    string Message,
    JsonObject? Data
);
