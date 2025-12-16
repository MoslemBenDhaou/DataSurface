namespace DataSurface.Admin.Dtos;

/// <summary>
/// Payload used to import dynamic entity definitions.
/// </summary>
public sealed class AdminImportPayloadDto
{
    /// <summary>
    /// Gets or sets the entities to import.
    /// </summary>
    public List<AdminEntityDefDto> Entities { get; set; } = new();
}

/// <summary>
/// Payload returned when exporting dynamic entity definitions.
/// </summary>
public sealed class AdminExportPayloadDto
{
    /// <summary>
    /// Gets or sets the exported entities.
    /// </summary>
    public List<AdminEntityDefDto> Entities { get; set; } = new();
}
