using CarDealer.Domain.Common;

namespace CarDealer.Domain.Entities;

/// <summary>
/// Grants a role to a user within one tenant. Composite key (UserId, RoleId, TenantId).
/// </summary>
/// <remarks>
/// The TenantId here is what makes permissions resolve per active tenant rather than
/// globally (acceptance criterion E6). The same user can be Admin in one tenant and
/// ReadOnly in another.
/// </remarks>
public class UserRole : ITenantScoped
{
    public long UserId { get; set; }

    public long RoleId { get; set; }

    public long TenantId { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    public User User { get; set; } = null!;

    public Role Role { get; set; } = null!;

    public Tenant Tenant { get; set; } = null!;
}
