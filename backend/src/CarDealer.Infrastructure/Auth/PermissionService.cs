using CarDealer.Domain.Entities;
using CarDealer.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CarDealer.Infrastructure.Auth;

public interface IPermissionService
{
    /// <summary>
    /// Resolves the permission codes a user holds in one specific tenant.
    /// </summary>
    Task<IReadOnlyList<string>> GetPermissionsAsync(long userId, long tenantId, CancellationToken ct);
}

/// <summary>
/// Resolves permissions from data (<see cref="Permission"/> / <see cref="RolePermission"/>),
/// never from role names (acceptance criterion E1).
/// </summary>
public sealed class PermissionService : IPermissionService
{
    private readonly CarDealerDbContext _db;

    public PermissionService(CarDealerDbContext db) => _db = db;

    public async Task<IReadOnlyList<string>> GetPermissionsAsync(
        long userId, long tenantId, CancellationToken ct)
    {
        // IgnoreQueryFilters is required and safe here: this runs during authentication,
        // before the tenant context exists, and the tenantId argument comes from a
        // membership check the caller has already performed - never from client input.
        // The explicit TenantId predicate below is what enforces scoping.
        return await _db.UserRoles
            .IgnoreQueryFilters()
            .Where(ur => ur.UserId == userId && ur.TenantId == tenantId)
            .SelectMany(ur => ur.Role.RolePermissions)
            .Select(rp => rp.Permission.Code)
            .Distinct()
            .OrderBy(code => code)
            .ToListAsync(ct)
            .ConfigureAwait(false);
    }
}
