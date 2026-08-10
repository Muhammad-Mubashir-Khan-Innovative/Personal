using System.ComponentModel.DataAnnotations;

namespace CarDealer.Api.Configuration;

/// <summary>
/// Rate limits for authentication endpoints (master prompt section 14, criterion I2).
/// </summary>
/// <remarks>
/// Configurable rather than constant because the right limit depends on deployment shape.
/// Partitioning is by remote IP, so an entire dealership behind one NAT address shares a
/// single bucket - a limit tuned for a home connection would throttle a legitimate office.
/// Deployments that sit behind a proxy should raise this and rely on the proxy's own limits.
/// </remarks>
public sealed class RateLimitOptions
{
    public const string SectionName = "RateLimits:Auth";

    [Range(1, 10_000)]
    public int PermitLimit { get; set; } = 10;

    [Range(1, 3600)]
    public int WindowSeconds { get; set; } = 60;
}
