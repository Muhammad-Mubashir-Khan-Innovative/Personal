using System.Security.Claims;
using CarDealer.Infrastructure.Auth;
using CarDealer.Infrastructure.Services;
using Serilog.Context;

namespace CarDealer.Api.Middleware;

/// <summary>
/// Resolves the active tenant from the authenticated principal, and only from there.
/// </summary>
/// <remarks>
/// SQL schema spec section 9: never trust a client-supplied TenantId. Acceptance criterion
/// C2 tests exactly this - a tenant id in a header, query string or body is ignored, because
/// this middleware reads nothing but the validated token's claim.
///
/// Requests without a resolvable tenant leave the context unresolved, which makes
/// ITenantContext.TenantIdOrZero return zero and every tenant query filter match nothing.
/// Fail closed.
/// </remarks>
public sealed class TenantResolutionMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<TenantResolutionMiddleware> _logger;

    public TenantResolutionMiddleware(RequestDelegate next, ILogger<TenantResolutionMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context, TenantContext tenantContext)
    {
        var user = context.User;

        if (user.Identity?.IsAuthenticated == true)
        {
            var tenantClaim = user.FindFirstValue(AppClaims.TenantId);

            if (long.TryParse(tenantClaim, out var tenantId) && tenantId > 0)
            {
                tenantContext.SetTenant(tenantId);
            }
            else
            {
                // An authenticated token with no usable tenant claim should not happen: every
                // token this service issues carries one. Log it rather than proceeding
                // silently, because it means either a token from another issuer or a bug.
                _logger.LogWarning(
                    "Authenticated request carried no usable {ClaimName} claim.", AppClaims.TenantId);
            }
        }

        var userId = user.FindFirstValue(System.IdentityModel.Tokens.Jwt.JwtRegisteredClaimNames.Sub);

        using (LogContext.PushProperty("TenantId", tenantContext.TenantIdOrZero))
        using (LogContext.PushProperty("UserId", userId ?? "anonymous"))
        {
            await _next(context).ConfigureAwait(false);
        }
    }
}
