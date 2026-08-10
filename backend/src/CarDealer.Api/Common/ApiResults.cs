using CarDealer.Api.Middleware;
using CarDealer.Application.Common;
using Microsoft.AspNetCore.Mvc;

namespace CarDealer.Api.Common;

/// <summary>
/// Maps application results onto the API's single error contract (criteria F2, F3, F4).
/// </summary>
public static class ApiResults
{
    /// <summary>
    /// Converts a failed <see cref="Result{T}"/> into a ProblemDetails response.
    /// </summary>
    /// <remarks>
    /// The ErrorKind to status-code mapping lives here alone, so that "authenticated but not
    /// permitted" cannot drift into 401 in one controller and 403 in another - criterion E3
    /// depends on that distinction being reliable.
    /// </remarks>
    public static IActionResult Problem<T>(this ControllerBase controller, Result<T> result)
    {
        var (status, title) = result.ErrorKind switch
        {
            ErrorKind.Validation => (StatusCodes.Status400BadRequest, "Validation failed"),
            ErrorKind.Unauthenticated => (StatusCodes.Status401Unauthorized, "Not authenticated"),
            ErrorKind.Forbidden => (StatusCodes.Status403Forbidden, "Not permitted"),
            ErrorKind.NotFound => (StatusCodes.Status404NotFound, "Not found"),
            ErrorKind.Conflict => (StatusCodes.Status409Conflict, "Conflict"),
            _ => (StatusCodes.Status500InternalServerError, "Unexpected error"),
        };

        var problem = new ProblemDetails
        {
            Status = status,
            Title = title,
            Detail = result.Error,
            Instance = controller.HttpContext.Request.Path,
        };

        problem.Extensions["correlationId"] = CorrelationIdOf(controller.HttpContext);

        return controller.StatusCode(status, problem);
    }

    public static string? CorrelationIdOf(HttpContext context)
        => context.Response.Headers.TryGetValue(CorrelationIdMiddleware.HeaderName, out var value)
            ? value.ToString()
            : null;
}
