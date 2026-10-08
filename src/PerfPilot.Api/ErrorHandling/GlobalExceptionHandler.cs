using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using PerfPilot.Core.Common;

namespace PerfPilot.Api.ErrorHandling;

/// <summary>Maps exceptions to RFC 7807 ProblemDetails responses.</summary>
public sealed class GlobalExceptionHandler(IProblemDetailsService problemDetails, ILogger<GlobalExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext context, Exception exception, CancellationToken ct)
    {
        ProblemDetails problem = exception switch
        {
            ValidationException v => new ValidationProblemDetails(v.Errors.ToDictionary(k => k.Key, k => k.Value))
            {
                Status = StatusCodes.Status400BadRequest,
                Title = "Validation failed",
                Detail = v.Errors.Count == 1 && v.Errors.ContainsKey("") ? v.Message : "One or more fields are invalid.",
            },
            NotFoundException => new ProblemDetails { Status = StatusCodes.Status404NotFound, Title = "Not found", Detail = exception.Message },
            ConflictException => new ProblemDetails { Status = StatusCodes.Status409Conflict, Title = "Conflict", Detail = exception.Message },
            ExternalCallException ext => new ProblemDetails
            {
                Status = StatusCodes.Status502BadGateway,
                Title = "Outbound call failed",
                Detail = ext.Message,
                Extensions = { ["errorKind"] = ext.ErrorKind },
            },
            BadHttpRequestException bad => new ProblemDetails { Status = bad.StatusCode, Title = "Bad request", Detail = bad.Message },
            OperationCanceledException when context.RequestAborted.IsCancellationRequested =>
                new ProblemDetails { Status = 499, Title = "Client closed request" },
            _ => new ProblemDetails
            {
                Status = StatusCodes.Status500InternalServerError,
                Title = "Unexpected error",
                Detail = "An unexpected error occurred. See the server log for details.",
            },
        };

        if (problem.Status >= 500)
        {
            logger.LogError(exception, "Unhandled exception for {Method} {Path}", context.Request.Method, context.Request.Path);
        }
        else
        {
            logger.LogInformation("{Method} {Path} returned {Status}: {Message}", context.Request.Method, context.Request.Path, problem.Status, exception.Message);
        }

        context.Response.StatusCode = problem.Status ?? 500;
        return await problemDetails.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = context,
            ProblemDetails = problem,
            Exception = exception,
        });
    }
}
