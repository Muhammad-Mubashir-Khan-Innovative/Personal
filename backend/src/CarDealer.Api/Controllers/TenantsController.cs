using Asp.Versioning;
using CarDealer.Api.Authorization;
using CarDealer.Application.Abstractions;
using CarDealer.Domain.Entities;
using CarDealer.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CarDealer.Api.Controllers;

[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/tenants")]
public sealed class TenantsController : ControllerBase
{
    private readonly CarDealerDbContext _db;
    private readonly ITenantContext _tenantContext;

    public TenantsController(CarDealerDbContext db, ITenantContext tenantContext)
    {
        _db = db;
        _tenantContext = tenantContext;
    }

    /// <summary>Returns the tenant the current access token is scoped to.</summary>
    /// <remarks>
    /// There is deliberately no endpoint that lists all tenants. The set of tenants a caller
    /// may enter comes from their own memberships, returned by /auth/login and /auth/me.
    /// </remarks>
    [HttpGet("current")]
    [HasPermission(Permissions.TenantsRead)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Current(CancellationToken ct)
    {
        var tenantId = _tenantContext.TenantId;

        var tenant = await _db.Tenants
            .Where(t => t.Id == tenantId)
            .Select(t => new
            {
                t.PublicId,
                t.Name,
                t.Slug,
                Status = t.Status.ToString(),
                t.DefaultCurrencyCode,
                t.DefaultCountryCode,
                t.CreatedAtUtc,
            })
            .FirstOrDefaultAsync(ct)
            .ConfigureAwait(false);

        return tenant is null ? NotFound() : Ok(tenant);
    }

    /// <summary>
    /// Lists users who are members of the active tenant.
    /// </summary>
    /// <remarks>
    /// The tenant scope comes from the query filter on TenantUsers, not from any parameter.
    /// This is the endpoint acceptance criteria C3 and C5 exercise: the same call made with
    /// two different tokens must return two disjoint result sets.
    /// </remarks>
    [HttpGet("current/members")]
    [HasPermission(Permissions.UsersRead)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> Members(CancellationToken ct)
    {
        var members = await _db.TenantUsers
            .Select(m => new
            {
                m.User.PublicId,
                m.User.Email,
                m.User.FirstName,
                m.User.LastName,
                MembershipStatus = m.MembershipStatus.ToString(),
                m.JoinedAtUtc,
            })
            .OrderBy(m => m.Email)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        return Ok(members);
    }
}
