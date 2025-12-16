using DataSurface.Core.ContractBuilderModels;

namespace DataSurface.EFCore.Options;

/// <summary>
/// Options for configuring DataSurface's Entity Framework Core integration.
/// </summary>
public sealed class DataSurfaceEfCoreOptions
{
    /// <summary>
    /// When <see langword="true"/>, automatically registers resource CLR types with the EF model based on
    /// discovered contracts.
    /// </summary>
    public bool AutoRegisterCrudEntities { get; set; } = true;

    // Conventions
    /// <summary>
    /// When <see langword="true"/>, applies a global query filter to exclude soft-deleted entities.
    /// </summary>
    /// <remarks>
    /// The default convention applies to entities implementing <c>ISoftDelete</c> and filters on <c>IsDeleted == false</c>.
    /// </remarks>
    public bool EnableSoftDeleteFilter { get; set; } = true;        // if entity has IsDeleted
    /// <summary>
    /// When <see langword="true"/>, configures a <c>RowVersion</c> <see cref="byte"/> array property as an EF rowversion.
    /// </summary>
    public bool EnableRowVersionConvention { get; set; } = true;    // if entity has RowVersion byte[]
    /// <summary>
    /// When <see langword="true"/>, automatically populates <c>CreatedAt</c> and <c>UpdatedAt</c> for entities
    /// implementing <see cref="Interfaces.ITimestamped"/>.
    /// </summary>
    public bool EnableTimestampConvention { get; set; } = true;     // if entity has ITimestamped
    /// <summary>
    /// When <see langword="true"/>, uses camelCase API names consistent with the Core contract builder conventions.
    /// </summary>
    public bool UseCamelCaseApiNames { get; set; } = true;          // consistent with Core builder

    // Discovery
    /// <summary>
    /// Assemblies to scan for resource types when building the static contract set.
    /// </summary>
    public IReadOnlyList<System.Reflection.Assembly> AssembliesToScan { get; init; } = [];

    // If you want to hide fields unless annotated (safe default)
    /// <summary>
    /// Options passed to the Core <c>ContractBuilder</c> when generating resource contracts from CLR types.
    /// </summary>
    public ContractBuilderOptions ContractBuilderOptions { get; init; } = new();
}
