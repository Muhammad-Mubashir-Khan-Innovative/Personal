using CarDealer.Api.Common;
using Microsoft.AspNetCore.Mvc;

namespace CarDealer.Api.Middleware;

/// <summary>
/// Turns unhandled exceptions into the same ProblemDetails contract used by handled
/// failures (criteria F3, F4).
/// </summary>
/// <remarks>
/// Outside Development the response carries no exception message, type or stack trace: an
/// exception message routinely contains a connection string, a file path or a query
/// fragment, and master prompt section 14 forbids leaking any of it. The correlation id is
/// what ties the caller's report back to the full server-side log entry.
/// </remarks>
public sealed class ExceptionHandlingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ExceptionHandlingMiddleware> _logger;
    private readonly IHostEnvironment _environment;

    public ExceptionHandlingMiddleware(
        RequestDelegate next,
        ILogger<ExceptionHandlingMiddleware> logger,
        IHostEnvironment environment)
    {
        _next = next;
        _logger = logger;
        _environment = environment;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            var correlationId = ApiResults.CorrelationIdOf(context);

            _logger.LogError(
                ex, "Unhandled exception for {Method} {Path}.", context.Request.Method, context.Request.Path);

            if (context.Response.HasStarted)
            {
                // Too late to replace the response; the log entry above is the record.
                throw;
            }

            var problem = new ProblemDetails
            {
                Status = StatusCodes.Status500InternalServerError,
                Title = "Unexpected error",
                Detail = _environment.IsDevelopment()
                    ? ex.ToString()
                    : "An unexpected error occurred. Quote the correlation id when reporting this.",
                Instance = context.Request.Path,
            };

            problem.Extensions["correlationId"] = correlationId;

            context.Response.Clear();
            context.Response.StatusCode = StatusCodes.Status500InternalServerError;
            context.Response.ContentType = "application/problem+json";

            await context.Response.WriteAsJsonAsync(problem).ConfigureAwait(false);
        }
    }
}
