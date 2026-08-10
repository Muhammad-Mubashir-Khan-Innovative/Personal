using CarDealer.Infrastructure.Services;

namespace CarDealer.UnitTests;

/// <summary>
/// The fail-closed contract every tenant query filter is built on.
/// </summary>
public sealed class TenantContextTests
{
    [Fact]
    public void Unresolved_context_reports_zero_not_a_real_tenant()
    {
        var context = new TenantContext();

        Assert.False(context.IsResolved);

        // Zero is never a valid tenant id, so a filter comparing against it matches nothing.
        // If this ever returned a real id - or if the filters compared against null with
        // different semantics - an unauthenticated request would read another tenant's data.
        Assert.Equal(0L, context.TenantIdOrZero);
    }

    [Fact]
    public void Reading_TenantId_before_resolution_throws()
    {
        var context = new TenantContext();

        // Loud failure beats a silent default: code that needs a tenant and has none has a
        // bug, and should not quietly operate on tenant zero.
        Assert.Throws<InvalidOperationException>(() => context.TenantId);
    }

    [Fact]
    public void Resolved_context_exposes_the_tenant()
    {
        var context = new TenantContext();

        context.SetTenant(42);

        Assert.True(context.IsResolved);
        Assert.Equal(42L, context.TenantId);
        Assert.Equal(42L, context.TenantIdOrZero);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Non_positive_tenant_ids_are_rejected(long tenantId)
    {
        var context = new TenantContext();

        // Accepting zero would make an "unresolved" context indistinguishable from one
        // resolved to tenant zero, collapsing the fail-closed guarantee.
        Assert.Throws<ArgumentOutOfRangeException>(() => context.SetTenant(tenantId));
    }
}
