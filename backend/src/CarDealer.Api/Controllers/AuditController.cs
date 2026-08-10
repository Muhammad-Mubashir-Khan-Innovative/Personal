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
[Route("api/v{version:apiVersion}/audit")]
public sealed class AuditController : ControllerBase
{
    private const int MaxPageSize = 200;

    private readonly CarDealerDbContext _db;

    public AuditController(CarDealerDbContext db) => _db = db;

    /// <summary>
    /// Reads the active tenant's audit log, newest first.
    /// </summary>
    /// <remarks>
    /// Scoped by the AuditLogs query filter, so entries with a null TenantId - system-level
    /// events such as a failed login before any tenant is chosen - are never visible to a
    /// tenant. This endpoint is how acceptance criterion G5 is verified.
    /// </remarks>
    [HttpGet]
    [HasPermission(Permissions.AuditRead)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> List(
        [FromQuery] string? action,
        [FromQuery] int take = 50,
        CancellationToken ct = default)
    {
        var pageSize = Math.Clamp(take, 1, MaxPageSize);

        var query = _db.AuditLogs.AsQueryable();

        if (!string.IsNullOrWhiteSpace(action))
        {
            query = query.Where(a => a.Action == action);
        }

        var entries = await query
            .OrderByDescending(a => a.CreatedAtUtc)
            .ThenByDescending(a => a.Id)
            .Take(pageSize)
            .Select(a => new
            {
                a.Id,
                a.Action,
                a.EntityType,
                a.EntityId,
                a.UserId,
                a.CorrelationId,
                a.IpAddress,
                a.MetadataJson,
                a.CreatedAtUtc,
            })
            .ToListAsync(ct)
            .ConfigureAwait(false);

        return Ok(entries);
    }
}
