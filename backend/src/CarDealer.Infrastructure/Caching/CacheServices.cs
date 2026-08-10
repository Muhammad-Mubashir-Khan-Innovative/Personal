using System.Text.Json;
using CarDealer.Application.Abstractions;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;

namespace CarDealer.Infrastructure.Caching;

/// <summary>
/// Cache backed by <see cref="IDistributedCache"/>, which is Redis in any environment that
/// configures it.
/// </summary>
public sealed class DistributedCacheService : ICacheService
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    private readonly IDistributedCache _cache;

    public DistributedCacheService(IDistributedCache cache) => _cache = cache;

    public async Task<T?> GetAsync<T>(string key, CancellationToken ct = default)
    {
        var payload = await _cache.GetStringAsync(key, ct).ConfigureAwait(false);

        return payload is null ? default : JsonSerializer.Deserialize<T>(payload, SerializerOptions);
    }

    public Task SetAsync<T>(string key, T value, TimeSpan? ttl = null, CancellationToken ct = default)
    {
        var options = new DistributedCacheEntryOptions();

        if (ttl.HasValue)
        {
            options.AbsoluteExpirationRelativeToNow = ttl.Value;
        }

        return _cache.SetStringAsync(key, JsonSerializer.Serialize(value, SerializerOptions), options, ct);
    }

    public Task RemoveAsync(string key, CancellationToken ct = default) => _cache.RemoveAsync(key, ct);
}

/// <summary>
/// Development-only in-process cache (master prompt section 4: "safe in-memory fallback only
/// for development").
/// </summary>
/// <remarks>
/// Acceptance criterion H2 requires that this never runs in Production. The guard lives in
/// <see cref="CarDealer.Infrastructure.DependencyInjection"/> and throws at startup rather
/// than here, so the failure is a refusal to boot rather than a service that looks healthy
/// while losing every entry per instance.
/// </remarks>
public sealed class InMemoryCacheService : ICacheService
{
    private readonly IMemoryCache _cache;

    public InMemoryCacheService(IMemoryCache cache) => _cache = cache;

    public Task<T?> GetAsync<T>(string key, CancellationToken ct = default)
        => Task.FromResult(_cache.TryGetValue(key, out var value) ? (T?)value : default);

    public Task SetAsync<T>(string key, T value, TimeSpan? ttl = null, CancellationToken ct = default)
    {
        using var entry = _cache.CreateEntry(key);
        entry.Value = value;

        if (ttl.HasValue)
        {
            entry.AbsoluteExpirationRelativeToNow = ttl.Value;
        }

        return Task.CompletedTask;
    }

    public Task RemoveAsync(string key, CancellationToken ct = default)
    {
        _cache.Remove(key);
        return Task.CompletedTask;
    }
}
