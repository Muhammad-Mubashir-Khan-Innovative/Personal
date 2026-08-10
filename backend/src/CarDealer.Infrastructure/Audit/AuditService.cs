using System.Text.Json;
using CarDealer.Application.Abstractions;
using CarDealer.Domain.Entities;
using CarDealer.Infrastructure.Persistence;

namespace CarDealer.Infrastructure.Audit;

public interface IAuditService
{
    /// <summary>
    /// Records an audited action (acceptance criterion G5).
    /// </summary>
    /// <remarks>
    /// <paramref name="metadata"/> must never carry secrets, tokens or passwords
    /// (criterion G6). Callers pass identifiers and outcomes, not credentials.
    /// </remarks>
    Task RecordAsync(
        string action,
        long? tenantId,
        long? userId,
        string? entityType = null,
        string? entityId = null,
        object? metadata = null,
        CancellationToken ct = default);
}

public sealed class AuditService : IAuditService
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    private readonly CarDealerDbContext _db;
    private readonly IDateTimeProvider _clock;
    private readonly ICorrelationContext _correlation;

    public AuditService(CarDealerDbContext db, IDateTimeProvider clock, ICorrelationContext correlation)
    {
        _db = db;
        _clock = clock;
        _correlation = correlation;
    }

    public async Task RecordAsync(
        string action,
        long? tenantId,
        long? userId,
        string? entityType = null,
        string? entityId = null,
        object? metadata = null,
        CancellationToken ct = default)
    {
        _db.AuditLogs.Add(new AuditLog
        {
            TenantId = tenantId,
            UserId = userId,
            Action = action,
            EntityType = entityType,
            EntityId = entityId,
            CorrelationId = _correlation.CorrelationId,
            IpAddress = _correlation.IpAddress,
            MetadataJson = metadata is null ? null : JsonSerializer.Serialize(metadata, SerializerOptions),
            CreatedAtUtc = _clock.UtcNow,
        });

        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
    }
}

/// <summary>
/// Per-request correlation and caller information, populated by middleware.
/// </summary>
public interface ICorrelationContext
{
    string? CorrelationId { get; }

    string? IpAddress { get; }

    void Set(string correlationId, string? ipAddress);
}

public sealed class CorrelationContext : ICorrelationContext
{
    public string? CorrelationId { get; private set; }

    public string? IpAddress { get; private set; }

    public void Set(string correlationId, string? ipAddress)
    {
        CorrelationId = correlationId;
        IpAddress = ipAddress;
    }
}
