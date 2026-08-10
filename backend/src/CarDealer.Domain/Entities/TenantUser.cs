using CarDealer.Domain.Common;
using CarDealer.Domain.Enums;

namespace CarDealer.Domain.Entities;

/// <summary>
/// A user's membership in one tenant (decision D2, schema delta section 2.1).
/// </summary>
/// <remarks>
/// Separate from <see cref="UserRole"/> on purpose. UserRole cannot express "invited but
/// holds no role yet" or "suspended in this tenant only", and User.Status is global, so
/// using it for per-tenant suspension would lock the user out everywhere.
/// </remarks>
public class TenantUser : AuditableEntity, ITenantScoped
{
    public long TenantId { get; set; }

    public long UserId { get; set; }

    public MembershipStatus MembershipStatus { get; set; } = MembershipStatus.Invited;

    public long? InvitedByUserId { get; set; }

    public DateTime? JoinedAtUtc { get; set; }

    public Tenant Tenant { get; set; } = null!;

    public User User { get; set; } = null!;

    /// <summary>
    /// The only status that grants access to this tenant. Used by authentication and by the
    /// assignment guard (schema delta section 2.3).
    /// </summary>
    public bool IsActive => MembershipStatus == MembershipStatus.Active;
}
