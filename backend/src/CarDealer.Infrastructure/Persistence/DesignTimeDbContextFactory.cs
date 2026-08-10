using CarDealer.Application.Abstractions;
using CarDealer.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace CarDealer.Infrastructure.Persistence;

/// <summary>
/// Builds a context for <c>dotnet ef</c> only.
/// </summary>
/// <remarks>
/// Without this, the EF tools would have to construct the application host, which runs
/// migrations and seeding on startup - so generating a migration would require the very
/// database being migrated to already exist.
///
/// The connection string here is never used to connect during scaffolding; EF needs a
/// provider to generate provider-specific SQL, not a reachable server. Override it with
/// CARDEALER_MIGRATIONS_CONNECTION when applying migrations from the CLI.
/// </remarks>
public sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<CarDealerDbContext>
{
    public CarDealerDbContext CreateDbContext(string[] args)
    {
        var connectionString =
            Environment.GetEnvironmentVariable("CARDEALER_MIGRATIONS_CONNECTION")
            ?? "Server=localhost,1433;Database=CarDealer;User Id=sa;Password=placeholder;TrustServerCertificate=True";

        var options = new DbContextOptionsBuilder<CarDealerDbContext>()
            .UseSqlServer(connectionString)
            .Options;

        return new CarDealerDbContext(options, new DesignTimeTenantContext(), new SystemDateTimeProvider());
    }

    /// <summary>
    /// Tenant context for scaffolding. Reports an unresolved tenant, which is what the query
    /// filters compile against - the generated SQL is identical either way.
    /// </summary>
    private sealed class DesignTimeTenantContext : ITenantContext
    {
        public bool IsResolved => false;

        public long TenantId => throw new InvalidOperationException(
            "No tenant is resolvable at design time.");

        public long TenantIdOrZero => 0L;

        public void SetTenant(long tenantId) => throw new NotSupportedException();
    }
}
