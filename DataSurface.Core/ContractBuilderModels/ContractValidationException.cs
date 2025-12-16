namespace DataSurface.Core.ContractBuilderModels;

/// <summary>
/// Exception thrown when one or more generated resource contracts fail validation.
/// </summary>
public sealed class ContractValidationException : Exception
{
    /// <summary>
    /// Gets the validation error messages.
    /// </summary>
    public IReadOnlyList<string> Errors { get; }

    /// <summary>
    /// Creates a new exception instance.
    /// </summary>
    /// <param name="errors">A list of validation error messages.</param>
    public ContractValidationException(IReadOnlyList<string> errors)
        : base("Resource contract validation failed:\n- " + string.Join("\n- ", errors))
    {
        Errors = errors;
    }
}