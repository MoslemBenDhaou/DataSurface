using DataSurface.Core.Annotations;
using DataSurface.Core.Contracts;

namespace DataSurface.Core.ContractBuilderModels;

/// <summary>
/// Options that control how CLR types and attributes are normalized into a unified <see cref="ResourceContract"/>.
/// </summary>
public sealed class ContractBuilderOptions
{
    /// <summary>
    /// When <see langword="true"/>, only properties explicitly annotated with <see cref="CrudFieldAttribute"/>
    /// participate as fields in the generated contract.
    /// </summary>
    /// <remarks>
    /// This enables an opt-in field allowlist, which is the recommended safe default.
    /// </remarks>
    public bool ExposeFieldsOnlyWhenAnnotated { get; set; } = true;

    /// <summary>
    /// When <see cref="ExposeFieldsOnlyWhenAnnotated"/> is <see langword="false"/>, controls whether scalar
    /// properties are included in the Read shape by default.
    /// </summary>
    /// <remarks>
    /// Properties hidden via <see cref="CrudHiddenAttribute"/> or a field marked as Hidden remain excluded.
    /// </remarks>
    public bool DefaultIncludeScalarsInRead { get; set; } = false;

    /// <summary>
    /// When <see langword="true"/>, API names are generated in camelCase from CLR property names.
    /// When <see langword="false"/>, CLR property names are used as-is (PascalCase).
    /// </summary>
    public bool UseCamelCaseApiNames { get; set; } = true;

    /// <summary>
    /// API route prefix (for example <c>"api"</c>), used by higher layers when generating endpoints.
    /// </summary>
    public string ApiPrefix { get; set; } = "api"; // used later; kept here for consistency
}