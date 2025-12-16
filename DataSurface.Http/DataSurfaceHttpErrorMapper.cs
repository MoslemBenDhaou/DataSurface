using DataSurface.EFCore.Exceptions;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace DataSurface.Http;

/// <summary>
/// Converts exceptions thrown by DataSurface components into HTTP error responses.
/// </summary>
public static class DataSurfaceHttpErrorMapper
{
    /// <summary>
    /// Maps an <see cref="Exception"/> to an <see cref="IResult"/> that can be returned from minimal API endpoints.
    /// </summary>
    /// <param name="ex">The exception to convert.</param>
    /// <param name="http">The current HTTP context, if available.</param>
    /// <returns>An <see cref="IResult"/> representing the error.</returns>
    public static IResult ToProblem(Exception ex, HttpContext? http = null)
    {
        // Validation
        if (ex is CrudRequestValidationException v)
        {
            return Results.Problem(
                title: "Validation failed",
                statusCode: StatusCodes.Status400BadRequest,
                extensions: new Dictionary<string, object?>
                {
                    ["errors"] = v.Errors
                });
        }

        // Not found
        if (ex is CrudNotFoundException)
            return Results.NotFound();

        // Concurrency
        if (ex is DbUpdateConcurrencyException)
        {
            return Results.Problem(
                title: "Concurrency conflict",
                statusCode: StatusCodes.Status409Conflict);
        }

        // Bad JSON / binding
        if (ex is BadHttpRequestException)
        {
            return Results.Problem(
                title: "Bad request",
                statusCode: StatusCodes.Status400BadRequest,
                detail: ex.Message);
        }

        // Fallback: don't expose internal exception details to clients
        // The exception should be logged server-side via ILogger
        return Results.Problem(
            title: "An unexpected error occurred",
            statusCode: StatusCodes.Status500InternalServerError);
    }
}
