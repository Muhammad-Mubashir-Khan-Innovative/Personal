using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using CarDealer.Application.Abstractions;
using CarDealer.Infrastructure.Auth;

namespace CarDealer.Api.Services;

/// <summary>
/// Reads the authenticated principal from the current HTTP request.
/// </summary>
/// <remarks>
/// Permissions come from the token's claims, which were resolved for the active tenant when
/// the token was issued (criterion E6). Because a token is scoped to one tenant, there is no
/// way for permissions from another tenant to appear here.
/// </remarks>
public sealed class CurrentUser : ICurrentUser
{
    private readonly IHttpContextAccessor _accessor;
    private IReadOnlySet<string>? _permissions;

    public CurrentUser(IHttpContextAccessor accessor) => _accessor = accessor;

    private ClaimsPrincipal? Principal => _accessor.HttpContext?.User;

    public bool IsAuthenticated => Principal?.Identity?.IsAuthenticated == true;

    public long? UserId
    {
        get
        {
            var value = Principal?.FindFirstValue(JwtRegisteredClaimNames.Sub);
            return long.TryParse(value, out var id) ? id : null;
        }
    }

    public string? Email => Principal?.FindFirstValue(JwtRegisteredClaimNames.Email);

    public IReadOnlySet<string> Permissions =>
        _permissions ??= Principal?.FindAll(AppClaims.Permission)
                             .Select(c => c.Value)
                             .ToHashSet(StringComparer.Ordinal)
                         ?? new HashSet<string>(StringComparer.Ordinal);

    public bool HasPermission(string permissionCode) => Permissions.Contains(permissionCode);
}
