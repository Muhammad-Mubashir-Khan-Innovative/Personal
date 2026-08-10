using CarDealer.Application.Abstractions;
using CarDealer.Domain.Common;
using CarDealer.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace CarDealer.Infrastructure.Persistence;

public class CarDealerDbContext : DbContext
{
    private readonly ITenantContext _tenantContext;
    private readonly IDateTimeProvider _clock;

    public CarDealerDbContext(
        DbContextOptions<CarDealerDbContext> options,
        ITenantContext tenantContext,
        IDateTimeProvider clock)
        : base(options)
    {
        _tenantContext = tenantContext;
        _clock = clock;
    }

    public DbSet<Tenant> Tenants => Set<Tenant>();

    public DbSet<User> Users => Set<User>();

    public DbSet<TenantUser> TenantUsers => Set<TenantUser>();

    public DbSet<Role> Roles => Set<Role>();

    public DbSet<UserRole> UserRoles => Set<UserRole>();

    public DbSet<Permission> Permissions => Set<Permission>();

    public DbSet<RolePermission> RolePermissions => Set<RolePermission>();

    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();

    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.ApplyConfigurationsFromAssembly(typeof(CarDealerDbContext).Assembly);

        ApplyTenantQueryFilters(modelBuilder);
    }

    /// <summary>
    /// Applies tenant isolation at the model level (SQL schema spec section 9).
    /// </summary>
    /// <remarks>
    /// Every filter compares against <see cref="ITenantContext.TenantIdOrZero"/>, which is
    /// zero when no tenant is resolved. Zero matches no tenant, so an unauthenticated or
    /// tenant-less request sees nothing rather than everything. Fail closed, not open.
    ///
    /// Entities NOT filtered here, each for a stated reason:
    ///   Tenant      - it is the tenant; access is controlled by membership at the service layer.
    ///   User        - a global identity by decision D2.
    ///   Permission  - global reference data, identical for every tenant.
    ///   RolePermission - reached only through Role, which is filtered.
    ///   RefreshToken - looked up by a cryptographically random hash during refresh, before
    ///                  any tenant is resolved. Filtering it would break the refresh flow;
    ///                  the token hash is itself the capability.
    /// </remarks>
    private void ApplyTenantQueryFilters(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<TenantUser>()
            .HasQueryFilter(e => e.TenantId == _tenantContext.TenantIdOrZero);

        modelBuilder.Entity<UserRole>()
            .HasQueryFilter(e => e.TenantId == _tenantContext.TenantIdOrZero);

        modelBuilder.Entity<AuditLog>()
            .HasQueryFilter(e => e.TenantId == _tenantContext.TenantIdOrZero);

        // System roles (TenantId null) are visible to every tenant; tenant-defined roles
        // only to their owner. This mirrors decision D1's shape and is the same pattern the
        // vehicle catalog will use in Phase 0.5.
        modelBuilder.Entity<Role>()
            .HasQueryFilter(e => e.TenantId == null || e.TenantId == _tenantContext.TenantIdOrZero);

        // Must mirror the Role filter exactly. Without it, RolePermission is queryable
        // without any tenant predicate, so another tenant's custom role composition would be
        // readable even though the Role row itself is hidden - the grants leak the shape of
        // a role we are deliberately concealing.
        modelBuilder.Entity<RolePermission>()
            .HasQueryFilter(e =>
                e.Role.TenantId == null || e.Role.TenantId == _tenantContext.TenantIdOrZero);
    }

    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        ApplyTimestamps();
        return base.SaveChangesAsync(cancellationToken);
    }

    public override int SaveChanges()
    {
        ApplyTimestamps();
        return base.SaveChanges();
    }

    private void ApplyTimestamps()
    {
        var now = _clock.UtcNow;

        foreach (var entry in ChangeTracker.Entries())
        {
            switch (entry.State)
            {
                case EntityState.Added:
                    if (entry.Entity is Entity added && added.CreatedAtUtc == default)
                    {
                        added.CreatedAtUtc = now;
                    }

                    if (entry.Entity is AuditableEntity addedAuditable)
                    {
                        addedAuditable.UpdatedAtUtc = now;
                    }

                    if (entry.Entity is UserRole addedUserRole && addedUserRole.CreatedAtUtc == default)
                    {
                        addedUserRole.CreatedAtUtc = now;
                    }

                    if (entry.Entity is RolePermission addedRolePermission
                        && addedRolePermission.CreatedAtUtc == default)
                    {
                        addedRolePermission.CreatedAtUtc = now;
                    }

                    break;

                case EntityState.Modified:
                    if (entry.Entity is AuditableEntity modified)
                    {
                        modified.UpdatedAtUtc = now;
                    }

                    break;
            }
        }
    }
}
