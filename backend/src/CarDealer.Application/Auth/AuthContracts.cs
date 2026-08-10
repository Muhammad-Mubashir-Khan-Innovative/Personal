using CarDealer.Application.Common;

namespace CarDealer.Application.Auth;

public sealed record LoginRequest(string Email, string Password, string? TenantSlug);

public sealed record RefreshRequest(string RefreshToken);

public sealed record LogoutRequest(string RefreshToken);

public sealed record SwitchTenantRequest(string TenantSlug);

/// <summary>A tenant the authenticated user may enter.</summary>
public sealed record TenantMembershipDto(Guid PublicId, string Slug, string Name);

/// <summary>
/// Login outcome.
/// </summary>
/// <remarks>
/// When a user belongs to more than one tenant and did not name one,
/// <see cref="RequiresTenantSelection"/> is true and no tokens are issued. An access token
/// is always scoped to exactly one tenant, so there is no meaningful token to hand back
/// before that choice is made.
/// </remarks>
public sealed record AuthResult(
    bool RequiresTenantSelection,
    string? AccessToken,
    string? RefreshToken,
    DateTime? AccessTokenExpiresAtUtc,
    TenantMembershipDto? ActiveTenant,
    IReadOnlyList<TenantMembershipDto> AvailableTenants,
    IReadOnlyList<string> Permissions);

public interface IAuthService
{
    Task<Result<AuthResult>> LoginAsync(LoginRequest request, string? ipAddress, CancellationToken ct = default);

    Task<Result<AuthResult>> RefreshAsync(RefreshRequest request, string? ipAddress, CancellationToken ct = default);

    Task<Result<bool>> LogoutAsync(LogoutRequest request, CancellationToken ct = default);

    /// <summary>
    /// Issues tokens for a different tenant the user belongs to.
    /// </summary>
    /// <param name="fromTenantId">
    /// The tenant the caller is currently in, recorded on the audit entry. An entry saying
    /// only that someone entered a tenant, without saying where from, is half a record.
    /// </param>
    Task<Result<AuthResult>> SwitchTenantAsync(
        long userId,
        SwitchTenantRequest request,
        long? fromTenantId,
        string? ipAddress,
        CancellationToken ct = default);
}
