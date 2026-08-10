namespace CarDealer.Application.Abstractions;

/// <summary>
/// Wall-clock access, injected so token expiry and rotation are testable without waiting.
/// </summary>
public interface IDateTimeProvider
{
    DateTime UtcNow { get; }
}

/// <summary>
/// Caching abstraction. Redis is one implementation; business logic never references it
/// directly (master prompt section 5, criterion H1).
/// </summary>
public interface ICacheService
{
    Task<T?> GetAsync<T>(string key, CancellationToken ct = default);

    Task SetAsync<T>(string key, T value, TimeSpan? ttl = null, CancellationToken ct = default);

    Task RemoveAsync(string key, CancellationToken ct = default);
}

/// <summary>
/// File and media storage abstraction with a local implementation for development
/// (criterion H3). Object storage replaces it without touching callers.
/// </summary>
public interface IFileStorage
{
    /// <summary>Stores content and returns an opaque storage reference.</summary>
    Task<string> SaveAsync(string container, string fileName, Stream content, CancellationToken ct = default);

    Task<Stream?> OpenAsync(string storageReference, CancellationToken ct = default);

    Task<bool> DeleteAsync(string storageReference, CancellationToken ct = default);
}

/// <summary>
/// Durable background job scheduling (criterion H4). Hangfire is the current backing store.
/// </summary>
public interface IBackgroundJobScheduler
{
    /// <summary>Enqueues work to run as soon as a worker is free. Survives restarts.</summary>
    string Enqueue<TJob>(string argument) where TJob : IBackgroundJob;

    string Schedule<TJob>(string argument, TimeSpan delay) where TJob : IBackgroundJob;
}

/// <summary>
/// A unit of background work. Implementations live in Infrastructure or Integrations.
/// </summary>
public interface IBackgroundJob
{
    Task ExecuteAsync(string argument, CancellationToken ct);
}

/// <summary>
/// Password hashing. Deliberately narrow so the algorithm can be replaced centrally.
/// </summary>
public interface IPasswordHasher
{
    string Hash(string password);

    PasswordVerificationOutcome Verify(string hash, string password);
}

public enum PasswordVerificationOutcome
{
    Failed = 0,
    Succeeded = 1,

    /// <summary>Correct password, but the stored hash uses outdated parameters.</summary>
    SucceededNeedsRehash = 2,
}
