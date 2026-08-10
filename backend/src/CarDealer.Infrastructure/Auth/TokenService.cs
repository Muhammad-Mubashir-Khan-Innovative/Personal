using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using CarDealer.Application.Abstractions;
using CarDealer.Domain.Entities;
using CarDealer.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace CarDealer.Infrastructure.Auth;

/// <summary>
/// Claim types this application puts on its access tokens.
/// </summary>
public static class AppClaims
{
    /// <summary>The active tenant. An access token is valid for exactly one tenant.</summary>
    public const string TenantId = "tenant_id";

    public const string TenantSlug = "tenant_slug";

    /// <summary>One claim per granted permission code, resolved for the active tenant.</summary>
    public const string Permission = "perm";
}

public sealed record IssuedTokens(
    string AccessToken,
    string RefreshToken,
    DateTime AccessTokenExpiresAtUtc,
    long RefreshTokenId);

public interface ITokenService
{
    Task<IssuedTokens> IssueAsync(
        User user, Tenant tenant, IReadOnlyCollection<string> permissions, string? ipAddress, CancellationToken ct);

    /// <summary>
    /// Validates a presented refresh token and returns the stored record, or null when the
    /// token is unknown.
    /// </summary>
    Task<RefreshToken?> FindByTokenAsync(string refreshToken, CancellationToken ct);

    /// <summary>
    /// Revokes an entire rotation chain from the given token onward.
    /// </summary>
    Task RevokeChainAsync(RefreshToken token, CancellationToken ct);

    Task RevokeAsync(RefreshToken token, CancellationToken ct);
}

public sealed class TokenService : ITokenService
{
    private readonly CarDealerDbContext _db;
    private readonly IDateTimeProvider _clock;
    private readonly JwtOptions _options;

    public TokenService(CarDealerDbContext db, IDateTimeProvider clock, IOptions<JwtOptions> options)
    {
        _db = db;
        _clock = clock;
        _options = options.Value;
    }

    public async Task<IssuedTokens> IssueAsync(
        User user,
        Tenant tenant,
        IReadOnlyCollection<string> permissions,
        string? ipAddress,
        CancellationToken ct)
    {
        var now = _clock.UtcNow;
        var accessExpiry = now.AddMinutes(_options.AccessTokenMinutes);

        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new(JwtRegisteredClaimNames.Email, user.Email),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString("N")),
            new(AppClaims.TenantId, tenant.Id.ToString()),
            new(AppClaims.TenantSlug, tenant.Slug),
        };

        claims.AddRange(permissions.Select(p => new Claim(AppClaims.Permission, p)));

        var key = new SymmetricSecurityKey(System.Text.Encoding.UTF8.GetBytes(_options.SigningKey));

        var jwt = new JwtSecurityToken(
            issuer: _options.Issuer,
            audience: _options.Audience,
            claims: claims,
            notBefore: now,
            expires: accessExpiry,
            signingCredentials: new SigningCredentials(key, SecurityAlgorithms.HmacSha256));

        var accessToken = new JwtSecurityTokenHandler().WriteToken(jwt);

        var (refreshToken, refreshHash) = GenerateRefreshToken();

        var entity = new RefreshToken
        {
            UserId = user.Id,
            TenantId = tenant.Id,
            TokenHash = refreshHash,
            ExpiresAtUtc = now.AddDays(_options.RefreshTokenDays),
            CreatedByIp = ipAddress,
            CreatedAtUtc = now,
        };

        _db.RefreshTokens.Add(entity);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);

        // Return the generated id so the caller can link the rotation chain precisely.
        // Deriving it by "most recent token for this user" would race with concurrent
        // refreshes and could chain the wrong pair.
        return new IssuedTokens(accessToken, refreshToken, accessExpiry, entity.Id);
    }

    public Task<RefreshToken?> FindByTokenAsync(string refreshToken, CancellationToken ct)
    {
        var hash = Hash(refreshToken);

        // No tenant filter: refresh happens before a tenant is resolved, and the token hash
        // is itself the capability. See CarDealerDbContext.ApplyTenantQueryFilters.
        return _db.RefreshTokens
            .Include(t => t.User)
            .FirstOrDefaultAsync(t => t.TokenHash == hash, ct);
    }

    /// <summary>
    /// Revokes the presented token and every token that descends from it.
    /// </summary>
    /// <remarks>
    /// Acceptance criterion D5. Presenting an already-rotated token means either an attacker
    /// replaying a stolen token or a client that lost the rotation - both warrant killing the
    /// whole chain, because we cannot tell which holder is legitimate.
    /// </remarks>
    public async Task RevokeChainAsync(RefreshToken token, CancellationToken ct)
    {
        var now = _clock.UtcNow;
        var current = token;

        // The chain is finite and acyclic by construction, but guard against a cycle
        // introduced by a bug rather than looping forever.
        var visited = new HashSet<long>();

        while (current is not null && visited.Add(current.Id))
        {
            if (current.RevokedAtUtc is null)
            {
                current.RevokedAtUtc = now;
            }

            var nextId = current.ReplacedByTokenId;

            if (nextId is null)
            {
                break;
            }

            current = await _db.RefreshTokens
                .FirstOrDefaultAsync(t => t.Id == nextId, ct)
                .ConfigureAwait(false);
        }

        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    public async Task RevokeAsync(RefreshToken token, CancellationToken ct)
    {
        token.RevokedAtUtc ??= _clock.UtcNow;
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Generates a 256-bit random refresh token and its SHA-256 hash. Only the hash is
    /// persisted (criterion D7).
    /// </summary>
    private static (string Token, byte[] Hash) GenerateRefreshToken()
    {
        var bytes = RandomNumberGenerator.GetBytes(32);
        var token = Convert.ToBase64String(bytes);

        return (token, Hash(token));
    }

    private static byte[] Hash(string token)
        => SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(token));
}
