using CarDealer.Application.Abstractions;
using CarDealer.Domain.Entities;
using Microsoft.AspNetCore.Identity;

namespace CarDealer.Infrastructure.Services;

public sealed class SystemDateTimeProvider : IDateTimeProvider
{
    public DateTime UtcNow => DateTime.UtcNow;
}

/// <summary>
/// Per-request tenant context, populated from the authenticated principal by middleware.
/// </summary>
/// <remarks>
/// Registered scoped. It is deliberately settable once by the auth pipeline rather than
/// constructed from configuration, because the tenant is a property of the request, not of
/// the process.
/// </remarks>
public sealed class TenantContext : ITenantContext
{
    private long? _tenantId;

    public bool IsResolved => _tenantId.HasValue;

    public long TenantId => _tenantId
        ?? throw new InvalidOperationException(
            "No tenant resolved for this request. Callers that legitimately run without a tenant "
            + "must use TenantIdOrZero or an explicit query-filter bypass.");

    public long TenantIdOrZero => _tenantId ?? 0L;

    public void SetTenant(long tenantId)
    {
        if (tenantId <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(tenantId), tenantId, "Tenant id must be positive.");
        }

        _tenantId = tenantId;
    }
}

/// <summary>
/// PBKDF2-HMAC-SHA512 with 100,000 iterations, via ASP.NET Core's hasher (criterion D1).
/// </summary>
/// <remarks>
/// Wrapped rather than used directly so the algorithm can be replaced in one place, and so
/// Application code never references an ASP.NET Core Identity type (criterion H6).
/// </remarks>
public sealed class IdentityPasswordHasher : IPasswordHasher
{
    private readonly PasswordHasher<User> _inner = new();

    // The User instance is unused by the v3 hasher but required by the API surface.
    private static readonly User HashSubject = new();

    public string Hash(string password) => _inner.HashPassword(HashSubject, password);

    public PasswordVerificationOutcome Verify(string hash, string password)
    {
        PasswordVerificationResult result;

        try
        {
            result = _inner.VerifyHashedPassword(HashSubject, hash, password);
        }
        catch (FormatException)
        {
            // A stored hash that is not valid base64 - corrupted, truncated, or written by
            // something other than this hasher. Treat it as a failed verification rather
            // than letting it become a 500: an authentication endpoint that throws on
            // certain accounts tells an attacker exactly which ones are interesting.
            return PasswordVerificationOutcome.Failed;
        }

        return result switch
        {
            PasswordVerificationResult.Success => PasswordVerificationOutcome.Succeeded,
            PasswordVerificationResult.SuccessRehashNeeded => PasswordVerificationOutcome.SucceededNeedsRehash,
            _ => PasswordVerificationOutcome.Failed,
        };
    }
}
