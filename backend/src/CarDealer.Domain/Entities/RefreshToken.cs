using CarDealer.Domain.Common;

namespace CarDealer.Domain.Entities;

/// <summary>
/// One issued refresh token. Rotation forms a chain via <see cref="ReplacedByTokenId"/>
/// (schema delta section 7).
/// </summary>
/// <remarks>
/// Master prompt section 14 requires a token revocation strategy. Expiry alone is not one -
/// acceptance criterion D5 requires that reusing an already-rotated token kills the entire
/// chain, which is what turns a stolen token into a detected intrusion rather than a silent
/// one.
/// </remarks>
public class RefreshToken : Entity
{
    public long UserId { get; set; }

    /// <summary>
    /// SHA-256 of the token. The token itself is returned to the caller once and never
    /// stored (criterion D7), so a database leak does not yield usable tokens.
    /// </summary>
    public byte[] TokenHash { get; set; } = [];

    /// <summary>The tenant this token was issued for. A token is valid for one tenant.</summary>
    public long TenantId { get; set; }

    public DateTime ExpiresAtUtc { get; set; }

    public DateTime? RevokedAtUtc { get; set; }

    /// <summary>Set when this token was rotated, pointing at its successor.</summary>
    public long? ReplacedByTokenId { get; set; }

    public string? CreatedByIp { get; set; }

    public RefreshToken? ReplacedByToken { get; set; }

    public User User { get; set; } = null!;

    public bool IsRevoked => RevokedAtUtc is not null;

    public bool IsExpired(DateTime utcNow) => ExpiresAtUtc <= utcNow;

    public bool IsUsable(DateTime utcNow) => !IsRevoked && !IsExpired(utcNow);
}
