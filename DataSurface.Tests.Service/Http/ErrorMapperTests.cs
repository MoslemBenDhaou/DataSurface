using DataSurface.EFCore.Exceptions;
using DataSurface.Http;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace DataSurface.Tests.Service.Http;

/// <summary>
/// Unit tests for <see cref="DataSurfaceHttpErrorMapper"/>.
/// Verifies that each exception type maps to the correct HTTP status code and problem details.
/// </summary>
public class ErrorMapperTests
{
    [Fact]
    public void ValidationException_Returns400()
    {
        var errors = new Dictionary<string, string[]>
        {
            ["name"] = new[] { "Field is required." }
        };
        var ex = new CrudRequestValidationException(errors);

        var result = DataSurfaceHttpErrorMapper.ToProblem(ex);

        var problem = ExtractProblemStatus(result);
        problem.Should().Be(StatusCodes.Status400BadRequest);
    }

    [Fact]
    public void NotFoundException_Returns404()
    {
        var ex = new CrudNotFoundException("products", 42);

        var result = DataSurfaceHttpErrorMapper.ToProblem(ex);

        ExtractProblemStatus(result).Should().Be(StatusCodes.Status404NotFound);
    }

    [Fact]
    public void ConcurrencyException_Returns409()
    {
        var ex = new CrudConcurrencyException("products", 1, "Entity modified");

        var result = DataSurfaceHttpErrorMapper.ToProblem(ex);

        ExtractProblemStatus(result).Should().Be(StatusCodes.Status409Conflict);
    }

    [Fact]
    public void DbUpdateConcurrencyException_Returns409()
    {
        var ex = new DbUpdateConcurrencyException("Concurrency conflict");

        var result = DataSurfaceHttpErrorMapper.ToProblem(ex);

        ExtractProblemStatus(result).Should().Be(StatusCodes.Status409Conflict);
    }

    [Fact]
    public void FormatException_Returns400()
    {
        var ex = new FormatException("Invalid GUID format");

        var result = DataSurfaceHttpErrorMapper.ToProblem(ex);

        ExtractProblemStatus(result).Should().Be(StatusCodes.Status400BadRequest);
    }

    [Fact]
    public void ArgumentException_Returns400()
    {
        var ex = new ArgumentException("Bad argument");

        var result = DataSurfaceHttpErrorMapper.ToProblem(ex);

        ExtractProblemStatus(result).Should().Be(StatusCodes.Status400BadRequest);
    }

    [Fact]
    public void InvalidOperationException_Returns400()
    {
        var ex = new InvalidOperationException("Operation 'Delete' is disabled");

        var result = DataSurfaceHttpErrorMapper.ToProblem(ex);

        ExtractProblemStatus(result).Should().Be(StatusCodes.Status400BadRequest);
    }

    [Fact]
    public void KeyNotFoundException_Returns404()
    {
        var ex = new KeyNotFoundException("Resource 'widgets' not found.");

        var result = DataSurfaceHttpErrorMapper.ToProblem(ex);

        ExtractProblemStatus(result).Should().Be(StatusCodes.Status404NotFound);
    }

    [Fact]
    public void UnauthorizedAccessException_Returns401()
    {
        var ex = new UnauthorizedAccessException("Not allowed");

        var result = DataSurfaceHttpErrorMapper.ToProblem(ex);

        ExtractProblemStatus(result).Should().Be(StatusCodes.Status401Unauthorized);
    }

    [Fact]
    public void OperationCanceledException_Returns499()
    {
        var ex = new OperationCanceledException();

        var result = DataSurfaceHttpErrorMapper.ToProblem(ex);

        ExtractProblemStatus(result).Should().Be(499);
    }

    [Fact]
    public void GenericException_Returns500()
    {
        var ex = new Exception("Something went wrong");

        var result = DataSurfaceHttpErrorMapper.ToProblem(ex);

        ExtractProblemStatus(result).Should().Be(StatusCodes.Status500InternalServerError);
    }

    [Fact]
    public void ValidationException_ContainsErrorsInExtensions()
    {
        var errors = new Dictionary<string, string[]>
        {
            ["name"] = new[] { "Required." },
            ["price"] = new[] { "Must be positive." }
        };
        var ex = new CrudRequestValidationException(errors);

        var result = DataSurfaceHttpErrorMapper.ToProblem(ex);

        // The result should be a ProblemHttpResult with extensions containing errors
        var problem = result as ProblemHttpResult;
        problem.Should().NotBeNull();
        problem!.ProblemDetails.Extensions.Should().ContainKey("errors");
    }

    /// <summary>
    /// Extracts the HTTP status code from a ProblemHttpResult.
    /// </summary>
    private static int? ExtractProblemStatus(IResult result)
    {
        if (result is ProblemHttpResult problem)
            return problem.StatusCode;
        return null;
    }
}
