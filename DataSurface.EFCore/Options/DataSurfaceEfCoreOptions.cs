using DataSurface.Core;
using DataSurface.Core.ContractBuilderModels;

namespace DataSurface.EFCore.Options;

/// <summary>
/// Options for configuring DataSurface's Entity Framework Core integration.
/// </summary>
public sealed class DataSurfaceEfCoreOptions
{
    /// <summary>
    /// Feature flags controlling which DataSurface capabilities are enabled.
    /// Use <see cref="DataSurfaceFeatures.Minimal"/>, <see cref="DataSurfaceFeatures.Standard"/>,
    /// or <see cref="DataSurfaceFeatures.Full"/> presets, or configure individual features.
    /// </summary>
    public DataSurfaceFeatures Features { get; set; } = DataSurfaceFeatures.Minimal;

    /// <summary>
    /// When <see langword="true"/>, automatically registers resource CLR types with the EF model based on
    /// discovered contracts.
    /// </summary>
    public bool AutoRegisterCrudEntities { get; set; } = false;

    // Conventions
    /// <summary>
    /// When <see langword="true"/>, applies a global query filter to exclude soft-deleted entities.
    /// </summary>
    /// <remarks>
    /// The default convention applies to entities implementing <c>ISoftDelete</c> and filters on <c>IsDeleted == false</c>.
    /// </remarks>
    public bool EnableSoftDeleteFilter { get; set; } = false;
    /// <summary>
    /// When <see langword="true"/>, configures a <c>RowVersion</c> <see cref="byte"/> array property as an EF rowversion.
    /// </summary>
    public bool EnableRowVersionConvention { get; set; } = false;
    /// <summary>
    /// When <see langword="true"/>, automatically populates <c>CreatedAt</c> and <c>UpdatedAt</c> for entities
    /// implementing <see cref="Interfaces.ITimestamped"/>.
    /// </summary>
    public bool EnableTimestampConvention { get; set; } = false;
    /// <summary>
    /// When <see langword="true"/>, uses camelCase API names consistent with the Core contract builder conventions.
    /// </summary>
    public bool UseCamelCaseApiNames { get; set; } = true;          // consistent with Core builder

    // Discovery
    /// <summary>
    /// Assemblies to scan for resource types when building the static contract set.
    /// </summary>
    public IReadOnlyList<System.Reflection.Assembly> AssembliesToScan { get; set; } = [];

    // If you want to hide fields unless annotated (safe default)
    /// <summary>
    /// Options passed to the Core <c>ContractBuilder</c> when generating resource contracts from CLR types.
    /// </summary>
    public ContractBuilderOptions ContractBuilderOptions { get; set; } = new();
}
