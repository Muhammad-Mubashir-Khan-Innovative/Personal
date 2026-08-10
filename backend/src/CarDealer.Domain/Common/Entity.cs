namespace CarDealer.Domain.Common;

/// <summary>
/// Base for all persisted entities. Internal transactional keys are bigint identity
/// (SQL schema spec section 2).
/// </summary>
public abstract class Entity
{
    public long Id { get; set; }

    public DateTime CreatedAtUtc { get; set; }
}

/// <summary>
/// Entities that record their last modification.
/// </summary>
public abstract class AuditableEntity : Entity
{
    public DateTime UpdatedAtUtc { get; set; }
}

/// <summary>
/// Marks an entity that is always owned by exactly one tenant.
/// </summary>
/// <remarks>
/// Deliberately distinct from <see cref="IOptionallyTenantScoped"/>. Decision D1 makes
/// TenantId nullable on the vehicle catalog tables only; everything else stays strictly
/// tenant-owned, and mixing the two up is how cross-tenant leaks happen.
/// </remarks>
public interface ITenantScoped
{
    long TenantId { get; set; }
}

/// <summary>
/// Marks an entity whose TenantId may be null, where null means the global catalog
/// shared across tenants (decision D1).
/// </summary>
/// <remarks>
/// Phase 0 defines this interface but has no implementors: the vehicle tables arrive in
/// Phase 0.5. It exists now so the distinction is part of the model from the start
/// rather than retrofitted onto entities that already assume non-null tenancy.
/// </remarks>
public interface IOptionallyTenantScoped
{
    long? TenantId { get; set; }
}
