namespace CarDealer.Domain.Enums;

// Value 0 is reserved for Unknown throughout, per docs/spec/03-canonical-vehicle-model.md
// section 6. A missing value must stay distinguishable from a real one.

public enum TenantStatus : byte
{
    Unknown = 0,
    Active = 1,
    Suspended = 2,
    Closed = 3,
}

/// <summary>
/// Global account state. NOT for per-tenant suspension.
/// </summary>
/// <remarks>
/// Decision D2: a user identity spans tenants, so suspending this would lock the user
/// out of every tenant they belong to. Per-tenant suspension is
/// <see cref="MembershipStatus.Suspended"/> on TenantUsers. Acceptance criterion C8.
/// </remarks>
public enum UserStatus : byte
{
    Unknown = 0,
    Active = 1,
    Suspended = 2,
    Deactivated = 3,
}

/// <summary>
/// A user's standing within one specific tenant (decision D2).
/// </summary>
public enum MembershipStatus : byte
{
    Unknown = 0,

    /// <summary>Invited but has not accepted. Cannot authenticate into this tenant.</summary>
    Invited = 1,

    /// <summary>Full member. The only status that permits access.</summary>
    Active = 2,

    /// <summary>Blocked from this tenant only; other memberships are unaffected.</summary>
    Suspended = 3,
}
