using System.Text.Json.Nodes;
using DataSurface.Core.Contracts;
using DataSurface.Core.Enums;

namespace DataSurface.EFCore.Interfaces;

/// <summary>
/// Provides field-level authorization by controlling which fields can be read or written.
/// </summary>
/// <remarks>
/// Implement this interface to dynamically show/hide fields based on user context.
/// This runs after the operation completes, allowing you to redact sensitive fields.
/// </remarks>
/// <example>
/// <code>
/// public class SensitiveFieldAuthorizer : IFieldAuthorizer
/// {
///     private readonly IHttpContextAccessor _http;
///     
///     public SensitiveFieldAuthorizer(IHttpContextAccessor http) => _http = http;
///     
///     public bool CanReadField(ResourceContract contract, string fieldName)
///     {
///         if (fieldName == "salary")
///             return _http.HttpContext?.User.IsInRole("HR") ?? false;
///         return true;
///     }
///     
///     public bool CanWriteField(ResourceContract contract, string fieldName, CrudOperation op)
///         => true; // Allow all writes
/// }
/// </code>
/// </example>
public interface IFieldAuthorizer
{
    /// <summary>
    /// Determines whether the current user can read a specific field.
    /// </summary>
    /// <param name="contract">The resource contract.</param>
    /// <param name="fieldName">The API field name.</param>
    /// <returns><see langword="true"/> if the field can be read; otherwise, <see langword="false"/>.</returns>
    bool CanReadField(ResourceContract contract, string fieldName);

    /// <summary>
    /// Determines whether the current user can write to a specific field.
    /// </summary>
    /// <param name="contract">The resource contract.</param>
    /// <param name="fieldName">The API field name.</param>
    /// <param name="operation">The CRUD operation (Create or Update).</param>
    /// <returns><see langword="true"/> if the field can be written; otherwise, <see langword="false"/>.</returns>
    bool CanWriteField(ResourceContract contract, string fieldName, CrudOperation operation);
}

/// <summary>
/// Extension methods for <see cref="IFieldAuthorizer"/>.
/// </summary>
public static class FieldAuthorizerExtensions
{
    /// <summary>
    /// Redacts fields from a JSON object based on field authorization.
    /// </summary>
    /// <param name="authorizer">The field authorizer.</param>
    /// <param name="contract">The resource contract.</param>
    /// <param name="obj">The JSON object to redact.</param>
    public static void RedactFields(this IFieldAuthorizer authorizer, ResourceContract contract, JsonObject obj)
    {
        if (authorizer is null || obj is null) return;

        var toRemove = new List<string>();
        foreach (var prop in obj)
        {
            if (!authorizer.CanReadField(contract, prop.Key))
                toRemove.Add(prop.Key);
        }

        foreach (var key in toRemove)
            obj.Remove(key);
    }

    /// <summary>
    /// Validates that all fields in the input can be written.
    /// </summary>
    /// <param name="authorizer">The field authorizer.</param>
    /// <param name="contract">The resource contract.</param>
    /// <param name="body">The JSON body to validate.</param>
    /// <param name="operation">The CRUD operation.</param>
    /// <returns>A list of unauthorized field names, or empty if all fields are authorized.</returns>
    public static IReadOnlyList<string> GetUnauthorizedWriteFields(
        this IFieldAuthorizer authorizer,
        ResourceContract contract,
        JsonObject body,
        CrudOperation operation)
    {
        if (authorizer is null || body is null) return [];

        var unauthorized = new List<string>();
        foreach (var prop in body)
        {
            if (!authorizer.CanWriteField(contract, prop.Key, operation))
                unauthorized.Add(prop.Key);
        }

        return unauthorized;
    }
}
