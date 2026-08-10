using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Asp.Versioning;
using CarDealer.Api.Common;
using CarDealer.Application.Abstractions;
using CarDealer.Application.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace CarDealer.Api.Controllers;

[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/auth")]
[EnableRateLimiting("auth")]
public sealed class AuthController : ControllerBase
{
    private readonly IAuthService _auth;
    private readonly ITenantContext _tenantContext;

    public AuthController(IAuthService auth, ITenantContext tenantContext)
    {
        _auth = auth;
        _tenantContext = tenantContext;
    }

    /// <summary>
    /// Authenticates a user and issues tokens for one tenant.
    /// </summary>
    /// <remarks>
    /// When the user belongs to several tenants and none is named, the response has
    /// requiresTenantSelection = true and lists the choices without issuing tokens. Call
    /// again with tenantSlug to complete the login.
    /// </remarks>
    [HttpPost("login")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(AuthResult), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> Login([FromBody] LoginRequest request, CancellationToken ct)
    {
        var result = await _auth.LoginAsync(request, RemoteIp(), ct).ConfigureAwait(false);

        return result.Succeeded ? Ok(result.Value) : this.Problem(result);
    }

    /// <summary>
    /// Exchanges a refresh token for a new pair, rotating the old one.
    /// </summary>
    /// <remarks>
    /// Presenting a token that was already rotated revokes the entire chain and returns 401.
    /// That is deliberate: a replayed token means either theft or a lost rotation, and the
    /// two are indistinguishable from here.
    /// </remarks>
    [HttpPost("refresh")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(AuthResult), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Refresh([FromBody] RefreshRequest request, CancellationToken ct)
    {
        var result = await _auth.RefreshAsync(request, RemoteIp(), ct).ConfigureAwait(false);

        return result.Succeeded ? Ok(result.Value) : this.Problem(result);
    }

    /// <summary>Revokes a refresh token.</summary>
    [HttpPost("logout")]
    [AllowAnonymous]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Logout([FromBody] LogoutRequest request, CancellationToken ct)
    {
        await _auth.LogoutAsync(request, ct).ConfigureAwait(false);

        // Always 204, whether or not the token existed: reporting otherwise would let an
        // unauthenticated caller probe for valid tokens.
        return NoContent();
    }

    /// <summary>
    /// Issues a new token pair for a different tenant the caller belongs to.
    /// </summary>
    /// <remarks>
    /// Tokens are scoped to one tenant, so switching means issuing a new one rather than
    /// mutating the existing token's scope. Every switch is audited (criterion C7).
    /// </remarks>
    [HttpPost("switch-tenant")]
    [Authorize]
    [ProducesResponseType(typeof(AuthResult), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> SwitchTenant(
        [FromBody] SwitchTenantRequest request, CancellationToken ct)
    {
        var userId = User.FindFirstValue(JwtRegisteredClaimNames.Sub);

        if (!long.TryParse(userId, out var id))
        {
            return Unauthorized();
        }

        // The source tenant comes from the resolved context, i.e. from the validated token -
        // never from the request body.
        long? fromTenantId = _tenantContext.IsResolved ? _tenantContext.TenantId : null;

        var result = await _auth.SwitchTenantAsync(id, request, fromTenantId, RemoteIp(), ct)
            .ConfigureAwait(false);

        return result.Succeeded ? Ok(result.Value) : this.Problem(result);
    }

    /// <summary>Returns the caller's identity, active tenant and effective permissions.</summary>
    [HttpGet("me")]
    [Authorize]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public IActionResult Me()
    {
        return Ok(new
        {
            userId = User.FindFirstValue(JwtRegisteredClaimNames.Sub),
            email = User.FindFirstValue(JwtRegisteredClaimNames.Email),
            tenantId = User.FindFirstValue(CarDealer.Infrastructure.Auth.AppClaims.TenantId),
            tenantSlug = User.FindFirstValue(CarDealer.Infrastructure.Auth.AppClaims.TenantSlug),
            permissions = User.FindAll(CarDealer.Infrastructure.Auth.AppClaims.Permission)
                .Select(c => c.Value)
                .OrderBy(c => c)
                .ToArray(),
        });
    }

    private string? RemoteIp() => HttpContext.Connection.RemoteIpAddress?.ToString();
}
