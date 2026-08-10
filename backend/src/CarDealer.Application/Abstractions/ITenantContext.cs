namespace CarDealer.Application.Abstractions;

/// <summary>
/// The tenant the current request is operating in, resolved from the authenticated token
/// and never from client input (SQL schema spec section 9, acceptance criterion C2).
/// </summary>
public interface ITenantContext
{
    /// <summary>True once a tenant has been resolved from the authenticated principal.</summary>
    bool IsResolved { get; }

    /// <summary>
    /// The active tenant. Throws when unresolved - callers that legitimately run without a
    /// tenant (login, tenant listing) must use <see cref="TenantIdOrZero"/> or an explicit
    /// filter bypass instead of guessing.
    /// </summary>
    long TenantId { get; }

    /// <summary>
    /// The active tenant, or zero when unresolved.
    /// </summary>
    /// <remarks>
    /// Zero is never a valid tenant id, so a query filtered on this value returns nothing
    /// when no tenant is resolved. That is deliberate: the failure mode of an unresolved
    /// tenant must be "no data", never "all data".
    /// </remarks>
    long TenantIdOrZero { get; }

    void SetTenant(long tenantId);
}
