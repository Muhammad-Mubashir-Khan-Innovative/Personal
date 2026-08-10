using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace CarDealer.IntegrationTests;

/// <summary>
/// Permission enforcement and tenant-scoped roles (acceptance section E).
/// </summary>
public sealed class AuthorizationTests : IClassFixture<ApiFactory>
{
    private readonly ApiFactory _factory;

    public AuthorizationTests(ApiFactory factory) => _factory = factory;

    [Fact]
    public async Task Anonymous_request_is_401_not_403()
    {
        var client = _factory.CreateApiClient();

        var response = await client.GetAsync("/api/v1/roles");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Authenticated_but_unpermitted_request_is_403_not_401()
    {
        // The distinction matters: 401 tells a client to re-authenticate, 403 tells it not to
        // bother. Collapsing them sends clients into pointless login loops.
        var client = await _factory.AuthenticatedClientAsync("readonly@nihon-motors.test");

        var response = await client.GetAsync("/api/v1/roles");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Permission_is_checked_not_role_name()
    {
        // ReadOnly holds tenants.read but not users.read, so the two endpoints must diverge
        // for the same principal. If authorization keyed off the role name, both would behave
        // identically.
        var client = await _factory.AuthenticatedClientAsync("readonly@nihon-motors.test");

        var permitted = await client.GetAsync("/api/v1/tenants/current");
        var refused = await client.GetAsync("/api/v1/tenants/current/members");

        Assert.Equal(HttpStatusCode.OK, permitted.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, refused.StatusCode);
    }

    [Fact]
    public async Task Permissions_resolve_per_tenant_not_globally()
    {
        // multi@example.test is Admin in Nihon and ReadOnly in Karachi.
        var inNihon = await _factory.AuthenticatedClientAsync("multi@example.test", "nihon-motors");
        var inKarachi = await _factory.AuthenticatedClientAsync("multi@example.test", "karachi-auto");

        var nihonMembers = await inNihon.GetAsync("/api/v1/tenants/current/members");
        var karachiMembers = await inKarachi.GetAsync("/api/v1/tenants/current/members");

        Assert.Equal(HttpStatusCode.OK, nihonMembers.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, karachiMembers.StatusCode);
    }

    [Fact]
    public async Task Two_tenants_can_define_a_role_with_the_same_name()
    {
        var nihon = await _factory.AuthenticatedClientAsync("owner@nihon-motors.test");
        var karachi = await _factory.AuthenticatedClientAsync("owner@karachi-auto.test");

        var name = "Regional Manager " + Guid.NewGuid().ToString("N")[..6];
        var body = new { name, description = "tenant defined", permissionCodes = new[] { "tenants.read" } };

        var first = await nihon.PostAsJsonAsync("/api/v1/roles", body);
        var second = await karachi.PostAsJsonAsync("/api/v1/roles", body);

        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        Assert.Equal(HttpStatusCode.Created, second.StatusCode);
    }

    [Fact]
    public async Task Duplicate_role_name_within_one_tenant_is_rejected()
    {
        var client = await _factory.AuthenticatedClientAsync("owner@nihon-motors.test");

        var name = "Duplicate " + Guid.NewGuid().ToString("N")[..6];
        var body = new { name, description = (string?)null, permissionCodes = new[] { "tenants.read" } };

        var first = await client.PostAsJsonAsync("/api/v1/roles", body);
        var second = await client.PostAsJsonAsync("/api/v1/roles", body);

        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
    }

    [Fact]
    public async Task Unknown_permission_codes_are_rejected_rather_than_dropped()
    {
        // Silently ignoring an unknown code would create a role that looks like it grants
        // something it does not - a security hole that reads as a typo.
        var client = await _factory.AuthenticatedClientAsync("owner@nihon-motors.test");

        var response = await client.PostAsJsonAsync("/api/v1/roles", new
        {
            name = "Bad " + Guid.NewGuid().ToString("N")[..6],
            description = (string?)null,
            permissionCodes = new[] { "tenants.read", "vehicles.launch_missiles" },
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Error_responses_carry_the_correlation_id()
    {
        var client = await _factory.AuthenticatedClientAsync("readonly@nihon-motors.test");

        var response = await client.GetAsync("/api/v1/roles");
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.True(response.Headers.Contains("X-Correlation-Id"));
        Assert.True(problem.TryGetProperty("correlationId", out var correlationId));
        Assert.False(string.IsNullOrWhiteSpace(correlationId.GetString()));
    }

    [Fact]
    public async Task Supplied_correlation_id_is_honored()
    {
        var client = _factory.CreateApiClient();
        client.DefaultRequestHeaders.Add("X-Correlation-Id", "upstream-trace-42");

        var response = await client.GetAsync("/health");

        Assert.Equal("upstream-trace-42", response.Headers.GetValues("X-Correlation-Id").Single());
    }

    [Theory]
    [InlineData("has spaces")]
    [InlineData("../../etc/passwd")]
    [InlineData("<script>alert(1)</script>")]
    public async Task Malformed_correlation_id_is_replaced(string supplied)
    {
        // The value reaches logs and the AuditLogs table, so unvalidated caller input has no
        // business being persisted verbatim.
        var client = _factory.CreateApiClient();
        client.DefaultRequestHeaders.Add("X-Correlation-Id", supplied);

        var response = await client.GetAsync("/health");
        var actual = response.Headers.GetValues("X-Correlation-Id").Single();

        Assert.NotEqual(supplied, actual);
        Assert.Matches("^[A-Za-z0-9_-]+$", actual);
    }
}
