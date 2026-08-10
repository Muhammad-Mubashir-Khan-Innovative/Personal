using CarDealer.Application.Abstractions;
using CarDealer.Application.Auth;
using CarDealer.Infrastructure.Audit;
using CarDealer.Infrastructure.Auth;
using CarDealer.Infrastructure.Caching;
using CarDealer.Infrastructure.Jobs;
using CarDealer.Infrastructure.Persistence;
using CarDealer.Infrastructure.Services;
using CarDealer.Infrastructure.Storage;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace CarDealer.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        services.AddDbContext<CarDealerDbContext>(options =>
            options.UseSqlServer(
                configuration.GetConnectionString("Default"),
                sql => sql.EnableRetryOnFailure()));

        services.AddScoped<TenantContext>();
        services.AddScoped<ITenantContext>(sp => sp.GetRequiredService<TenantContext>());

        services.AddScoped<CorrelationContext>();
        services.AddScoped<ICorrelationContext>(sp => sp.GetRequiredService<CorrelationContext>());

        services.AddSingleton<IDateTimeProvider, SystemDateTimeProvider>();
        services.AddSingleton<IPasswordHasher, IdentityPasswordHasher>();

        services.AddScoped<ITokenService, TokenService>();
        services.AddScoped<IPermissionService, PermissionService>();
        services.AddScoped<IAuthService, AuthService>();
        services.AddScoped<IAuditService, AuditService>();
        services.AddScoped<DatabaseSeeder>();

        services.AddOptions<JwtOptions>()
            .Bind(configuration.GetSection(JwtOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.Configure<LocalFileStorageOptions>(
            configuration.GetSection(LocalFileStorageOptions.SectionName));

        services.AddSingleton<IFileStorage, LocalFileStorage>();

        AddCaching(services, configuration, environment);

        services.AddScoped<IBackgroundJobScheduler, HangfireJobScheduler>();
        services.AddScoped<EchoJob>();

        return services;
    }

    /// <summary>
    /// Registers the cache, enforcing that the in-memory fallback is development-only.
    /// </summary>
    /// <remarks>
    /// Acceptance criterion H2. Master prompt section 4 permits a "safe in-memory fallback
    /// only for development". Outside Development a missing Redis connection string is a
    /// startup failure, not a silent downgrade: an in-process cache in production looks
    /// healthy while losing every entry per instance, which is far worse than refusing to
    /// boot.
    /// </remarks>
    private static void AddCaching(
        IServiceCollection services, IConfiguration configuration, IHostEnvironment environment)
    {
        var redisConnection = configuration.GetConnectionString("Redis");

        if (!string.IsNullOrWhiteSpace(redisConnection))
        {
            services.AddStackExchangeRedisCache(options =>
            {
                options.Configuration = redisConnection;
                options.InstanceName = "cardealer:";
            });

            services.AddScoped<ICacheService, DistributedCacheService>();
            return;
        }

        if (!environment.IsDevelopment())
        {
            throw new InvalidOperationException(
                "No Redis connection string is configured. The in-memory cache fallback is "
                + "permitted only in Development (master prompt section 4). Set "
                + "ConnectionStrings__Redis for this environment.");
        }

        services.AddMemoryCache();
        services.AddScoped<ICacheService, InMemoryCacheService>();
    }
}
