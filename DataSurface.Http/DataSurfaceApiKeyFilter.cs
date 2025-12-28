using Microsoft.AspNetCore.Http;

namespace DataSurface.Http;

/// <summary>
/// Endpoint filter that validates API key authentication for DataSurface endpoints.
/// </summary>
public class DataSurfaceApiKeyFilter : IEndpointFilter
{
    private readonly DataSurfaceHttpOptions _options;
    private readonly IApiKeyValidator? _validator;

    /// <summary>
    /// Creates a new API key filter.
    /// </summary>
    /// <param name="options">HTTP options containing API key configuration.</param>
    /// <param name="validator">Optional custom validator for API keys.</param>
    public DataSurfaceApiKeyFilter(DataSurfaceHttpOptions options, IApiKeyValidator? validator = null)
    {
        _options = options;
        _validator = validator;
    }

    /// <inheritdoc />
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        if (!_options.EnableApiKeyAuth)
            return await next(context);

        var httpContext = context.HttpContext;
        var headerName = _options.ApiKeyHeaderName;

        if (!httpContext.Request.Headers.TryGetValue(headerName, out var apiKeyValues) || 
            string.IsNullOrWhiteSpace(apiKeyValues.FirstOrDefault()))
        {
            return Results.Unauthorized();
        }

        var apiKey = apiKeyValues.First()!;

        // Use custom validator if registered, otherwise accept any non-empty key
        // (In production, users should register their own IApiKeyValidator)
        if (_validator is not null)
        {
            var isValid = await _validator.ValidateAsync(apiKey, httpContext.RequestAborted);
            if (!isValid)
                return Results.Unauthorized();
        }

        return await next(context);
    }
}

/// <summary>
/// Interface for validating API keys.
/// </summary>
public interface IApiKeyValidator
{
    /// <summary>
    /// Validates an API key.
    /// </summary>
    /// <param name="apiKey">The API key to validate.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>True if the API key is valid; otherwise false.</returns>
    Task<bool> ValidateAsync(string apiKey, CancellationToken ct = default);
}
