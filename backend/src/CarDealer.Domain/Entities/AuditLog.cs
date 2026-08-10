using CarDealer.Domain.Common;

namespace CarDealer.Domain.Entities;

/// <summary>
/// Technical security record of a critical action (master prompt section 14).
/// </summary>
/// <remarks>
/// This is NOT the CRM activity timeline. That is a separate, user-facing feature arriving
/// in Phase 1 as CustomerActivities (schema delta section 7). Conflating them produces an
/// audit log that gets filtered for presentation, which defeats its purpose.
///
/// TenantId is nullable because some audited actions have no tenant context - a failed login
/// before any tenant is selected, for instance.
/// </remarks>
public class AuditLog : Entity
{
    public long? TenantId { get; set; }

    public long? UserId { get; set; }

    public string Action { get; set; } = string.Empty;

    public string? EntityType { get; set; }

    public string? EntityId { get; set; }

    public string? CorrelationId { get; set; }

    public string? IpAddress { get; set; }

    /// <summary>
    /// Structured detail. Must never contain secrets, tokens or passwords (criterion G6).
    /// </summary>
    public string? MetadataJson { get; set; }
}

/// <summary>
/// Audited action names. Acceptance criterion G5 requires each of these to produce a row.
/// </summary>
public static class AuditActions
{
    public const string LoginSucceeded = "auth.login.succeeded";
    public const string LoginFailed = "auth.login.failed";
    public const string Logout = "auth.logout";
    public const string TokenRefreshed = "auth.token.refreshed";
    public const string TokenReuseDetected = "auth.token.reuse_detected";
    public const string TenantSwitched = "auth.tenant.switched";

    public const string RoleCreated = "role.created";
    public const string RoleUpdated = "role.updated";
    public const string RoleDeleted = "role.deleted";
    public const string RolePermissionsChanged = "role.permissions.changed";

    public const string UserInvited = "user.invited";
    public const string UserRoleAssigned = "user.role.assigned";
    public const string UserRoleRemoved = "user.role.removed";
    public const string UserMembershipSuspended = "user.membership.suspended";
    public const string UserMembershipReactivated = "user.membership.reactivated";
}
