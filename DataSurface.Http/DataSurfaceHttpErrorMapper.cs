using System.Diagnostics;
using DataSurface.EFCore.Exceptions;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

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
        // Get trace ID for correlation
        var traceId = Activity.Current?.Id ?? http?.TraceIdentifier;
        
        // Try to get logger and environment from DI
        var logger = http?.RequestServices?.GetService<ILogger<DataSurfaceHttpOptions>>();
        var env = http?.RequestServices?.GetService<IHostEnvironment>();
        var isDevelopment = env?.IsDevelopment() ?? false;
        
        // Build base extensions with trace ID
        var extensions = new Dictionary<string, object?>
        {
            ["traceId"] = traceId
        };

        // Validation errors (400)
        if (ex is CrudRequestValidationException v)
        {
            logger?.LogWarning("Validation failed for request {TraceId}: {Errors}", traceId, v.Errors);
            extensions["errors"] = v.Errors;
            return Results.Problem(
                title: "Validation failed",
                detail: "One or more validation errors occurred.",
                statusCode: StatusCodes.Status400BadRequest,
                extensions: extensions);
        }

        // Not found (404)
        if (ex is CrudNotFoundException nf)
        {
            logger?.LogDebug("Resource not found for request {TraceId}: {Message}", traceId, nf.Message);
            return Results.Problem(
                title: "Resource not found",
                detail: nf.Message,
                statusCode: StatusCodes.Status404NotFound,
                extensions: extensions);
        }

        // Custom concurrency exception (409)
        if (ex is CrudConcurrencyException cc)
        {
            logger?.LogWarning("Concurrency conflict for request {TraceId}: {Resource} id={Id}", 
                traceId, cc.ResourceKey, cc.Id);
            return Results.Problem(
                title: "Concurrency conflict",
                detail: cc.Message,
                statusCode: StatusCodes.Status409Conflict,
                extensions: extensions);
        }

        // EF Core concurrency exception (409)
        if (ex is DbUpdateConcurrencyException dbConcurrency)
        {
            logger?.LogWarning(dbConcurrency, "Database concurrency conflict for request {TraceId}", traceId);
            return Results.Problem(
                title: "Concurrency conflict",
                detail: "The record was modified by another request. Please refresh and try again.",
                statusCode: StatusCodes.Status409Conflict,
                extensions: extensions);
        }

        // Database constraint violations (409 for unique, 400 for FK, etc.)
        if (ex is DbUpdateException dbUpdate)
        {
            logger?.LogWarning(dbUpdate, "Database update failed for request {TraceId}: {Message}", 
                traceId, dbUpdate.InnerException?.Message ?? dbUpdate.Message);
            
            var detail = "A database constraint was violated.";
            var innerMsg = dbUpdate.InnerException?.Message?.ToLowerInvariant() ?? "";
            
            if (innerMsg.Contains("unique") || innerMsg.Contains("duplicate"))
            {
                detail = "A record with the same unique value already exists.";
                return Results.Problem(
                    title: "Duplicate record",
                    detail: isDevelopment ? dbUpdate.InnerException?.Message ?? detail : detail,
                    statusCode: StatusCodes.Status409Conflict,
                    extensions: extensions);
            }
            
            if (innerMsg.Contains("foreign key") || innerMsg.Contains("reference"))
            {
                detail = "The operation violates a foreign key constraint. Ensure referenced records exist.";
            }
            
            return Results.Problem(
                title: "Database constraint violation",
                detail: isDevelopment ? dbUpdate.InnerException?.Message ?? detail : detail,
                statusCode: StatusCodes.Status400BadRequest,
                extensions: extensions);
        }

        // Bad JSON / binding (400)
        if (ex is BadHttpRequestException badReq)
        {
            logger?.LogWarning("Bad HTTP request for {TraceId}: {Message}", traceId, badReq.Message);
            return Results.Problem(
                title: "Bad request",
                detail: badReq.Message,
                statusCode: StatusCodes.Status400BadRequest,
                extensions: extensions);
        }

        // Invalid ID format (400) - e.g., "abc" for an integer ID
        if (ex is FormatException formatEx)
        {
            logger?.LogWarning("Invalid format for request {TraceId}: {Message}", traceId, formatEx.Message);
            return Results.Problem(
                title: "Invalid format",
                detail: "The provided value has an invalid format. " + formatEx.Message,
                statusCode: StatusCodes.Status400BadRequest,
                extensions: extensions);
        }

        // Argument exceptions (400)
        if (ex is ArgumentException argEx)
        {
            logger?.LogWarning("Invalid argument for request {TraceId}: {Message}", traceId, argEx.Message);
            return Results.Problem(
                title: "Invalid argument",
                detail: argEx.Message,
                statusCode: StatusCodes.Status400BadRequest,
                extensions: extensions);
        }

        // Operation not allowed / disabled (400)
        if (ex is InvalidOperationException invalidOp)
        {
            logger?.LogWarning("Invalid operation for request {TraceId}: {Message}", traceId, invalidOp.Message);
            return Results.Problem(
                title: "Operation not allowed",
                detail: invalidOp.Message,
                statusCode: StatusCodes.Status400BadRequest,
                extensions: extensions);
        }

        // Resource key not found (404)
        if (ex is KeyNotFoundException keyNotFound)
        {
            logger?.LogWarning("Key not found for request {TraceId}: {Message}", traceId, keyNotFound.Message);
            return Results.Problem(
                title: "Resource not found",
                detail: keyNotFound.Message,
                statusCode: StatusCodes.Status404NotFound,
                extensions: extensions);
        }

        // Unauthorized (401) - if we add auth exceptions later
        if (ex is UnauthorizedAccessException unauth)
        {
            logger?.LogWarning("Unauthorized access for request {TraceId}: {Message}", traceId, unauth.Message);
            return Results.Problem(
                title: "Unauthorized",
                detail: "You are not authorized to perform this operation.",
                statusCode: StatusCodes.Status401Unauthorized,
                extensions: extensions);
        }

        // Operation cancelled (client disconnected)
        if (ex is OperationCanceledException)
        {
            logger?.LogDebug("Operation cancelled for request {TraceId}", traceId);
            return Results.Problem(
                title: "Request cancelled",
                detail: "The request was cancelled.",
                statusCode: 499, // Client Closed Request
                extensions: extensions);
        }

        // Fallback: log the full exception and return a safe error message
        logger?.LogError(ex, 
            "Unhandled exception for request {TraceId} at {Path}: {ExceptionType} - {Message}", 
            traceId, 
            http?.Request.Path.Value ?? "unknown",
            ex.GetType().Name,
            ex.Message);

        // In development, include exception details for debugging
        if (isDevelopment)
        {
            extensions["exceptionType"] = ex.GetType().FullName;
            extensions["stackTrace"] = ex.StackTrace;
            return Results.Problem(
                title: "An unexpected error occurred",
                detail: ex.Message,
                statusCode: StatusCodes.Status500InternalServerError,
                extensions: extensions);
        }

        // In production, return a safe generic message
        return Results.Problem(
            title: "An unexpected error occurred",
            detail: $"An internal server error occurred. Please contact support with trace ID: {traceId}",
            statusCode: StatusCodes.Status500InternalServerError,
            extensions: extensions);
    }
}
