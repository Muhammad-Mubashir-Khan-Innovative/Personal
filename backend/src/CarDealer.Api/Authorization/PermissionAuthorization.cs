using CarDealer.Infrastructure.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;

namespace CarDealer.Api.Authorization;

/// <summary>
/// Requires a permission code, resolved from the active tenant's grants.
/// </summary>
/// <remarks>
/// Acceptance criterion E1: authorization checks a permission, never a role name. A tenant
/// that invents its own role gets working authorization with no code change.
/// </remarks>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true)]
public sealed class HasPermissionAttribute : AuthorizeAttribute
{
    public const string PolicyPrefix = "perm:";

    public HasPermissionAttribute(string permissionCode) => Policy = PolicyPrefix + permissionCode;
}

/// <summary>
/// Builds permission policies on demand so that adding a permission never means registering
/// a policy by hand.
/// </summary>
public sealed class PermissionPolicyProvider : IAuthorizationPolicyProvider
{
    private readonly DefaultAuthorizationPolicyProvider _fallback;

    public PermissionPolicyProvider(IOptions<AuthorizationOptions> options)
        => _fallback = new DefaultAuthorizationPolicyProvider(options);

    public Task<AuthorizationPolicy> GetDefaultPolicyAsync() => _fallback.GetDefaultPolicyAsync();

    public Task<AuthorizationPolicy?> GetFallbackPolicyAsync() => _fallback.GetFallbackPolicyAsync();

    public Task<AuthorizationPolicy?> GetPolicyAsync(string policyName)
    {
        if (!policyName.StartsWith(HasPermissionAttribute.PolicyPrefix, StringComparison.Ordinal))
        {
            return _fallback.GetPolicyAsync(policyName);
        }

        var permissionCode = policyName[HasPermissionAttribute.PolicyPrefix.Length..];

        // RequireAuthenticatedUser first, so an anonymous caller yields 401 rather than 403.
        // Criterion E3 depends on these staying distinguishable.
        var policy = new AuthorizationPolicyBuilder()
            .RequireAuthenticatedUser()
            .RequireClaim(AppClaims.Permission, permissionCode)
            .Build();

        return Task.FromResult<AuthorizationPolicy?>(policy);
    }
}
