using CarDealer.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CarDealer.Infrastructure.Persistence.Configurations;

public class RefreshTokenConfiguration : IEntityTypeConfiguration<RefreshToken>
{
    public void Configure(EntityTypeBuilder<RefreshToken> builder)
    {
        builder.ToTable("RefreshTokens");
        builder.HasKey(x => x.Id);

        // varbinary(32): SHA-256 of the token. The raw token is never persisted
        // (acceptance criterion D7).
        builder.Property(x => x.TokenHash)
            .HasColumnType("varbinary(32)")
            .IsRequired();

        builder.Property(x => x.ExpiresAtUtc).HasPrecision(3).IsRequired();
        builder.Property(x => x.RevokedAtUtc).HasPrecision(3);
        builder.Property(x => x.CreatedByIp).HasMaxLength(45); // IPv6 length
        builder.Property(x => x.CreatedAtUtc).HasPrecision(3).IsRequired();

        builder.HasOne(x => x.User)
            .WithMany()
            .HasForeignKey(x => x.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne<Tenant>()
            .WithMany()
            .HasForeignKey(x => x.TenantId)
            .OnDelete(DeleteBehavior.Restrict);

        // Self-reference forming the rotation chain. Restrict, never cascade: deleting a
        // token must not silently erase the chain that proves reuse.
        builder.HasOne(x => x.ReplacedByToken)
            .WithMany()
            .HasForeignKey(x => x.ReplacedByTokenId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(x => x.TokenHash).IsUnique();
        builder.HasIndex(x => new { x.UserId, x.ExpiresAtUtc });
    }
}

public class AuditLogConfiguration : IEntityTypeConfiguration<AuditLog>
{
    public void Configure(EntityTypeBuilder<AuditLog> builder)
    {
        builder.ToTable("AuditLogs");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.Action).HasMaxLength(128).IsRequired();
        builder.Property(x => x.EntityType).HasMaxLength(128);
        builder.Property(x => x.EntityId).HasMaxLength(64);
        builder.Property(x => x.CorrelationId).HasMaxLength(64);
        builder.Property(x => x.IpAddress).HasMaxLength(45);
        builder.Property(x => x.MetadataJson);
        builder.Property(x => x.CreatedAtUtc).HasPrecision(3).IsRequired();

        // Restrict on both: audit history must survive the deletion of what it describes.
        // SQL schema spec section 6 - never cascade-delete audit or history.
        builder.HasOne<Tenant>()
            .WithMany()
            .HasForeignKey(x => x.TenantId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(x => x.UserId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(x => new { x.TenantId, x.CreatedAtUtc });
        builder.HasIndex(x => new { x.TenantId, x.Action, x.CreatedAtUtc });
    }
}
