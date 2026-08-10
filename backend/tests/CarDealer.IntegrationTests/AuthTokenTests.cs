using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using CarDealer.Domain.Entities;
using CarDealer.Domain.Enums;
using CarDealer.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace CarDealer.IntegrationTests;

/// <summary>
/// Token rotation and revocation (acceptance section D).
/// </summary>
public sealed class AuthTokenTests : IClassFixture<ApiFactory>
{
    private readonly ApiFactory _factory;

    public AuthTokenTests(ApiFactory factory) => _factory = factory;

    [Fact]
    public async Task Refresh_rotates_and_invalidates_the_previous_token()
    {
        var client = _factory.CreateApiClient();
        var login = await _factory.LoginAsync(client, "owner@nihon-motors.test");

        var first = login.RefreshToken!;
        var rotated = await RefreshAsync(client, first);

        Assert.Equal(HttpStatusCode.OK, rotated.Status);
        Assert.NotEqual(first, rotated.RefreshToken);

        var replayed = await RefreshAsync(client, first);
        Assert.Equal(HttpStatusCode.Unauthorized, replayed.Status);
    }

    /// <summary>
    /// The property that separates real revocation from mere expiry (criterion D5).
    /// </summary>
    /// <remarks>
    /// Replaying a rotated token means either theft or a client that lost the rotation, and
    /// the server cannot tell which. Killing the whole chain is the only safe response - so
    /// the token that was still legitimately live must die too.
    /// </remarks>
    [Fact]
    public async Task Reusing_a_rotated_token_revokes_the_entire_chain()
    {
        var client = _factory.CreateApiClient();
        var login = await _factory.LoginAsync(client, "sales@nihon-motors.test");

        var first = login.RefreshToken!;
        var second = (await RefreshAsync(client, first)).RefreshToken!;
        var third = (await RefreshAsync(client, second)).RefreshToken!;

        // Replay the oldest token, which was rotated away two steps ago.
        var replay = await RefreshAsync(client, first);
        Assert.Equal(HttpStatusCode.Unauthorized, replay.Status);

        // The currently-live token is now dead as well.
        var afterBreach = await RefreshAsync(client, third);
        Assert.Equal(HttpStatusCode.Unauthorized, afterBreach.Status);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CarDealerDbContext>();

        var userId = await db.Users
            .Where(u => u.Email == "sales@nihon-motors.test")
            .Select(u => u.Id)
            .FirstAsync();

        var live = await db.RefreshTokens
            .Where(t => t.UserId == userId && t.RevokedAtUtc == null)
            .CountAsync();

        Assert.Equal(0, live);
    }

    [Fact]
    public async Task Reuse_detection_is_audited()
    {
        var client = _factory.CreateApiClient();
        var login = await _factory.LoginAsync(client, "readonly@nihon-motors.test");

        var first = login.RefreshToken!;
        await RefreshAsync(client, first);
        await RefreshAsync(client, first);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CarDealerDbContext>();

        var recorded = await db.AuditLogs
            .IgnoreQueryFilters()
            .AnyAsync(a => a.Action == AuditActions.TokenReuseDetected);

        Assert.True(recorded, "Token reuse must leave an audit trail.");
    }

    [Fact]
    public async Task Logout_revokes_the_refresh_token()
    {
        var client = _factory.CreateApiClient();
        var login = await _factory.LoginAsync(client, "owner@karachi-auto.test");

        var response = await client.PostAsJsonAsync(
            "/api/v1/auth/logout", new { refreshToken = login.RefreshToken });

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        var afterLogout = await RefreshAsync(client, login.RefreshToken!);
        Assert.Equal(HttpStatusCode.Unauthorized, afterLogout.Status);
    }

    [Fact]
    public async Task Logout_with_an_unknown_token_still_reports_success()
    {
        // Reporting "no such token" would let an unauthenticated caller probe for valid ones.
        var client = _factory.CreateApiClient();

        var response = await client.PostAsJsonAsync(
            "/api/v1/auth/logout", new { refreshToken = "not-a-real-token" });

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    [Fact]
    public async Task Refresh_token_is_never_stored_in_plaintext()
    {
        var client = _factory.CreateApiClient();
        var login = await _factory.LoginAsync(client, "owner@nihon-motors.test");

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CarDealerDbContext>();

        var hashes = await db.RefreshTokens.Select(t => t.TokenHash).ToListAsync();

        Assert.NotEmpty(hashes);
        Assert.All(hashes, h => Assert.Equal(32, h.Length)); // SHA-256

        var plaintext = System.Text.Encoding.UTF8.GetBytes(login.RefreshToken!);
        Assert.DoesNotContain(hashes, h => h.SequenceEqual(plaintext));
    }

    [Fact]
    public async Task Access_token_carries_the_active_tenant()
    {
        var client = _factory.CreateApiClient();
        var login = await _factory.LoginAsync(client, "multi@example.test", "karachi-auto");

        var claims = DecodeClaims(login.AccessToken!);

        Assert.Equal("karachi-auto", claims.GetProperty("tenant_slug").GetString());
        Assert.True(claims.TryGetProperty("tenant_id", out _));

        // Regression guard: with JwtBearer's inbound claim mapping left on, "sub" is rewritten
        // to a WS-Federation URI and every lookup of it silently returns null - which surfaces
        // as a spurious 401 on any endpoint that needs the user id.
        Assert.True(claims.TryGetProperty("sub", out var sub));
        Assert.False(string.IsNullOrWhiteSpace(sub.GetString()));
    }

    [Fact]
    public async Task Membership_revoked_after_issue_kills_the_refresh()
    {
        var client = _factory.CreateApiClient();
        var login = await _factory.LoginAsync(client, "multi@example.test", "karachi-auto");

        try
        {
            await SetMembershipAsync("multi@example.test", "karachi-auto", MembershipStatus.Suspended);

            // A live refresh token must not outlive the membership that justified it.
            var refreshed = await RefreshAsync(client, login.RefreshToken!);
            Assert.Equal(HttpStatusCode.Forbidden, refreshed.Status);
        }
        finally
        {
            // Restore the seeded state. The class fixture is shared, so leaving this user
            // suspended silently breaks whichever later test happens to log into this tenant -
            // an order-dependent failure that looks like a bug in the code under test.
            await SetMembershipAsync("multi@example.test", "karachi-auto", MembershipStatus.Active);
        }
    }

    private async Task SetMembershipAsync(string email, string tenantSlug, MembershipStatus status)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CarDealerDbContext>();

        var membership = await db.TenantUsers
            .IgnoreQueryFilters()
            .Include(m => m.User)
            .Include(m => m.Tenant)
            .FirstAsync(m => m.User.Email == email && m.Tenant.Slug == tenantSlug);

        membership.MembershipStatus = status;
        await db.SaveChangesAsync();
    }

    private static async Task<(HttpStatusCode Status, string? RefreshToken)> RefreshAsync(
        HttpClient client, string token)
    {
        var response = await client.PostAsJsonAsync("/api/v1/auth/refresh", new { refreshToken = token });

        if (!response.IsSuccessStatusCode)
        {
            return (response.StatusCode, null);
        }

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();

        return (response.StatusCode, body.GetProperty("refreshToken").GetString());
    }

    private static JsonElement DecodeClaims(string jwt)
    {
        var payload = jwt.Split('.')[1];
        payload = payload.PadRight(payload.Length + ((4 - (payload.Length % 4)) % 4), '=');

        var json = System.Text.Encoding.UTF8.GetString(
            Convert.FromBase64String(payload.Replace('-', '+').Replace('_', '/')));

        return JsonDocument.Parse(json).RootElement.Clone();
    }
}
