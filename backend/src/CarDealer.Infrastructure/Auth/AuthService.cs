using CarDealer.Application.Abstractions;
using CarDealer.Application.Auth;
using CarDealer.Application.Common;
using CarDealer.Domain.Entities;
using CarDealer.Domain.Enums;
using CarDealer.Infrastructure.Audit;
using CarDealer.Infrastructure.Persistence;
using CarDealer.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;

namespace CarDealer.Infrastructure.Auth;

public sealed class AuthService : IAuthService
{
    private readonly CarDealerDbContext _db;
    private readonly IPasswordHasher _passwordHasher;
    private readonly ITokenService _tokens;
    private readonly IPermissionService _permissions;
    private readonly IAuditService _audit;
    private readonly IDateTimeProvider _clock;

    public AuthService(
        CarDealerDbContext db,
        IPasswordHasher passwordHasher,
        ITokenService tokens,
        IPermissionService permissions,
        IAuditService audit,
        IDateTimeProvider clock)
    {
        _db = db;
        _passwordHasher = passwordHasher;
        _tokens = tokens;
        _permissions = permissions;
        _audit = audit;
        _clock = clock;
    }

    public async Task<Result<AuthResult>> LoginAsync(
        LoginRequest request, string? ipAddress, CancellationToken ct = default)
    {
        var email = request.Email.Trim().ToLowerInvariant();

        var user = await _db.Users.FirstOrDefaultAsync(u => u.Email == email, ct).ConfigureAwait(false);

        // Verify a dummy hash when the user is absent so that a missing account and a wrong
        // password take comparable time. Without this, response timing enumerates accounts.
        if (user is null)
        {
            _passwordHasher.Verify(DummyHash, request.Password);
            await _audit.RecordAsync(
                AuditActions.LoginFailed, null, null, metadata: new { email, reason = "unknown_user" }, ct: ct)
                .ConfigureAwait(false);

            return Unauthenticated();
        }

        if (_passwordHasher.Verify(user.PasswordHash, request.Password) == PasswordVerificationOutcome.Failed)
        {
            await _audit.RecordAsync(
                AuditActions.LoginFailed, null, user.Id, metadata: new { reason = "bad_password" }, ct: ct)
                .ConfigureAwait(false);

            return Unauthenticated();
        }

        if (user.Status != UserStatus.Active)
        {
            await _audit.RecordAsync(
                AuditActions.LoginFailed, null, user.Id, metadata: new { reason = "account_not_active" }, ct: ct)
                .ConfigureAwait(false);

            return Result<AuthResult>.Failure(ErrorKind.Forbidden, "This account is not active.");
        }

        var memberships = await GetActiveMembershipsAsync(user.Id, ct).ConfigureAwait(false);

        if (memberships.Count == 0)
        {
            await _audit.RecordAsync(
                AuditActions.LoginFailed, null, user.Id, metadata: new { reason = "no_active_membership" }, ct: ct)
                .ConfigureAwait(false);

            return Result<AuthResult>.Failure(
                ErrorKind.Forbidden, "This account has no active tenant membership.");
        }

        Tenant? target;

        if (!string.IsNullOrWhiteSpace(request.TenantSlug))
        {
            target = memberships.FirstOrDefault(t =>
                string.Equals(t.Slug, request.TenantSlug, StringComparison.OrdinalIgnoreCase));

            // Deliberately Forbidden rather than NotFound: the caller is authenticated, and
            // distinguishing "no such tenant" from "not your tenant" would leak tenant names.
            if (target is null)
            {
                return Result<AuthResult>.Failure(
                    ErrorKind.Forbidden, "No active membership in the requested tenant.");
            }
        }
        else if (memberships.Count == 1)
        {
            target = memberships[0];
        }
        else
        {
            // More than one tenant and no choice made. An access token is scoped to exactly
            // one tenant, so there is no correct token to issue yet.
            return Result<AuthResult>.Success(new AuthResult(
                RequiresTenantSelection: true,
                AccessToken: null,
                RefreshToken: null,
                AccessTokenExpiresAtUtc: null,
                ActiveTenant: null,
                AvailableTenants: memberships.Select(ToDto).ToList(),
                Permissions: []));
        }

        user.LastLoginAtUtc = _clock.UtcNow;

        var (result, _) = await IssueAsync(user, target, memberships, ipAddress, ct).ConfigureAwait(false);

        await _audit.RecordAsync(
            AuditActions.LoginSucceeded, target.Id, user.Id, ct: ct).ConfigureAwait(false);

        return Result<AuthResult>.Success(result);
    }

    public async Task<Result<AuthResult>> RefreshAsync(
        RefreshRequest request, string? ipAddress, CancellationToken ct = default)
    {
        var stored = await _tokens.FindByTokenAsync(request.RefreshToken, ct).ConfigureAwait(false);

        if (stored is null)
        {
            return Unauthenticated();
        }

        var now = _clock.UtcNow;

        // Criterion D5: a token that was already rotated or revoked is being replayed.
        // We cannot tell an attacker from a client that lost the rotation, so the entire
        // chain dies and every session derived from it is invalidated.
        if (stored.IsRevoked || stored.ReplacedByTokenId is not null)
        {
            await _tokens.RevokeChainAsync(stored, ct).ConfigureAwait(false);

            await _audit.RecordAsync(
                AuditActions.TokenReuseDetected,
                stored.TenantId,
                stored.UserId,
                entityType: nameof(RefreshToken),
                entityId: stored.Id.ToString(),
                ct: ct).ConfigureAwait(false);

            return Unauthenticated();
        }

        if (stored.IsExpired(now))
        {
            return Unauthenticated();
        }

        var membership = await FindActiveMembershipAsync(stored.UserId, stored.TenantId, ct)
            .ConfigureAwait(false);

        // Membership can be revoked while a refresh token is still alive. Re-check on every
        // refresh rather than trusting the token's original grant.
        if (membership is null)
        {
            await _tokens.RevokeChainAsync(stored, ct).ConfigureAwait(false);
            return Result<AuthResult>.Failure(
                ErrorKind.Forbidden, "Membership in this tenant is no longer active.");
        }

        var memberships = await GetActiveMembershipsAsync(stored.UserId, ct).ConfigureAwait(false);

        var (issued, successorId) = await IssueAsync(stored.User, membership, memberships, ipAddress, ct)
            .ConfigureAwait(false);

        // Link the old token to its successor, forming the chain that makes reuse detectable.
        stored.ReplacedByTokenId = successorId;
        stored.RevokedAtUtc = now;
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);

        await _audit.RecordAsync(
            AuditActions.TokenRefreshed, membership.Id, stored.UserId, ct: ct).ConfigureAwait(false);

        return Result<AuthResult>.Success(issued);
    }

    public async Task<Result<bool>> LogoutAsync(LogoutRequest request, CancellationToken ct = default)
    {
        var stored = await _tokens.FindByTokenAsync(request.RefreshToken, ct).ConfigureAwait(false);

        // Always report success: whether a token existed is not information an unauthenticated
        // caller should be able to probe for.
        if (stored is null)
        {
            return Result<bool>.Success(true);
        }

        await _tokens.RevokeAsync(stored, ct).ConfigureAwait(false);

        await _audit.RecordAsync(
            AuditActions.Logout, stored.TenantId, stored.UserId, ct: ct).ConfigureAwait(false);

        return Result<bool>.Success(true);
    }

    public async Task<Result<AuthResult>> SwitchTenantAsync(
        long userId,
        SwitchTenantRequest request,
        long? fromTenantId,
        string? ipAddress,
        CancellationToken ct = default)
    {
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == userId, ct).ConfigureAwait(false);

        if (user is null || user.Status != UserStatus.Active)
        {
            return Unauthenticated();
        }

        var memberships = await GetActiveMembershipsAsync(userId, ct).ConfigureAwait(false);

        var target = memberships.FirstOrDefault(t =>
            string.Equals(t.Slug, request.TenantSlug, StringComparison.OrdinalIgnoreCase));

        if (target is null)
        {
            return Result<AuthResult>.Failure(
                ErrorKind.Forbidden, "No active membership in the requested tenant.");
        }

        var (issued, _) = await IssueAsync(user, target, memberships, ipAddress, ct).ConfigureAwait(false);

        // Criterion C7: every tenant switch is audited.
        //
        // Recorded against the DESTINATION tenant: the tenant being entered is the one with a
        // security interest in knowing someone arrived. The source is carried in the metadata
        // so the entry answers "who came in, and from where".
        var fromSlug = fromTenantId is null
            ? null
            : await _db.Tenants
                .Where(t => t.Id == fromTenantId)
                .Select(t => t.Slug)
                .FirstOrDefaultAsync(ct)
                .ConfigureAwait(false);

        await _audit.RecordAsync(
            AuditActions.TenantSwitched,
            target.Id,
            userId,
            entityType: nameof(Tenant),
            entityId: target.Id.ToString(),
            metadata: new { toSlug = target.Slug, fromSlug, fromTenantId },
            ct: ct).ConfigureAwait(false);

        return Result<AuthResult>.Success(issued);
    }

    private async Task<(AuthResult Result, long RefreshTokenId)> IssueAsync(
        User user,
        Tenant target,
        IReadOnlyList<Tenant> memberships,
        string? ipAddress,
        CancellationToken ct)
    {
        var permissions = await _permissions.GetPermissionsAsync(user.Id, target.Id, ct).ConfigureAwait(false);

        var tokens = await _tokens.IssueAsync(user, target, permissions, ipAddress, ct).ConfigureAwait(false);

        var result = new AuthResult(
            RequiresTenantSelection: false,
            AccessToken: tokens.AccessToken,
            RefreshToken: tokens.RefreshToken,
            AccessTokenExpiresAtUtc: tokens.AccessTokenExpiresAtUtc,
            ActiveTenant: ToDto(target),
            AvailableTenants: memberships.Select(ToDto).ToList(),
            Permissions: permissions);

        return (result, tokens.RefreshTokenId);
    }

    /// <summary>
    /// Tenants the user may currently enter.
    /// </summary>
    /// <remarks>
    /// IgnoreQueryFilters is required: this runs before a tenant is resolved, so the filter
    /// would match nothing. Scoping comes from the explicit UserId predicate, and only
    /// Active memberships are returned - Invited and Suspended are excluded, which is what
    /// makes per-tenant suspension work (criterion C8).
    /// </remarks>
    private async Task<IReadOnlyList<Tenant>> GetActiveMembershipsAsync(long userId, CancellationToken ct)
        => await _db.TenantUsers
            .IgnoreQueryFilters()
            .Where(m => m.UserId == userId && m.MembershipStatus == MembershipStatus.Active)
            .Select(m => m.Tenant)
            .Where(t => t.Status == TenantStatus.Active)
            .OrderBy(t => t.Name)
            .ToListAsync(ct)
            .ConfigureAwait(false);

    private async Task<Tenant?> FindActiveMembershipAsync(long userId, long tenantId, CancellationToken ct)
        => await _db.TenantUsers
            .IgnoreQueryFilters()
            .Where(m => m.UserId == userId
                        && m.TenantId == tenantId
                        && m.MembershipStatus == MembershipStatus.Active)
            .Select(m => m.Tenant)
            .Where(t => t.Status == TenantStatus.Active)
            .FirstOrDefaultAsync(ct)
            .ConfigureAwait(false);

    private static TenantMembershipDto ToDto(Tenant tenant)
        => new(tenant.PublicId, tenant.Slug, tenant.Name);

    private static Result<AuthResult> Unauthenticated()
        => Result<AuthResult>.Failure(ErrorKind.Unauthenticated, "Invalid credentials.");

    /// <summary>
    /// A genuine PBKDF2 hash, computed once, used only to equalise timing on the
    /// unknown-user path.
    /// </summary>
    /// <remarks>
    /// Computed lazily rather than written as a literal so it is always a valid hash for
    /// whatever algorithm <see cref="IPasswordHasher"/> currently uses. A hard-coded literal
    /// would silently stop costing anything the moment the algorithm changed, quietly
    /// restoring the timing side channel.
    /// </remarks>
    private static readonly Lazy<string> DummyHashValue = new(
        () => new IdentityPasswordHasher().Hash("timing-equalisation-only"),
        LazyThreadSafetyMode.ExecutionAndPublication);

    private static string DummyHash => DummyHashValue.Value;
}
