using CarDealer.Infrastructure.Audit;
using Serilog.Context;

namespace CarDealer.Api.Middleware;

/// <summary>
/// Assigns a correlation id to every request and echoes it back to the caller
/// (acceptance criteria G3, G4).
/// </summary>
/// <remarks>
/// A caller-supplied id is honored rather than replaced, so a correlation id issued by an
/// upstream service survives the hop. Incoming values are length-capped and stripped of
/// anything outside a conservative character set - the value ends up in logs and in the
/// AuditLogs table, and unvalidated caller input has no business in either.
/// </remarks>
public sealed class CorrelationIdMiddleware
{
    public const string HeaderName = "X-Correlation-Id";

    private const int MaxLength = 64;

    private readonly RequestDelegate _next;

    public CorrelationIdMiddleware(RequestDelegate next) => _next = next;

    public async Task InvokeAsync(HttpContext context, CorrelationContext correlationContext)
    {
        var correlationId = ResolveCorrelationId(context);
        var ipAddress = context.Connection.RemoteIpAddress?.ToString();

        correlationContext.Set(correlationId, ipAddress);

        context.Response.Headers[HeaderName] = correlationId;

        using (LogContext.PushProperty("CorrelationId", correlationId))
        {
            await _next(context).ConfigureAwait(false);
        }
    }

    private static string ResolveCorrelationId(HttpContext context)
    {
        if (!context.Request.Headers.TryGetValue(HeaderName, out var supplied))
        {
            return Guid.NewGuid().ToString("N");
        }

        var candidate = supplied.ToString();

        if (string.IsNullOrWhiteSpace(candidate) || candidate.Length > MaxLength)
        {
            return Guid.NewGuid().ToString("N");
        }

        foreach (var c in candidate)
        {
            if (!char.IsAsciiLetterOrDigit(c) && c != '-' && c != '_')
            {
                return Guid.NewGuid().ToString("N");
            }
        }

        return candidate;
    }
}
