using CarDealer.Application.Abstractions;
using Microsoft.Extensions.Options;

namespace CarDealer.Infrastructure.Storage;

public sealed class LocalFileStorageOptions
{
    public const string SectionName = "Storage:Local";

    public string RootPath { get; set; } = "./storage";
}

/// <summary>
/// Filesystem-backed storage for local development (criterion H3).
/// </summary>
/// <remarks>
/// Returns opaque storage references rather than paths, so that swapping in object storage
/// does not change any caller. Media handling proper arrives in Phase 1, and how third-party
/// images are stored is still an open legal question (open item O1).
/// </remarks>
public sealed class LocalFileStorage : IFileStorage
{
    private readonly string _rootPath;

    public LocalFileStorage(IOptions<LocalFileStorageOptions> options)
        => _rootPath = Path.GetFullPath(options.Value.RootPath);

    public async Task<string> SaveAsync(
        string container, string fileName, Stream content, CancellationToken ct = default)
    {
        var safeContainer = Sanitize(container);
        var safeFileName = Sanitize(fileName);

        var directory = Path.Combine(_rootPath, safeContainer);
        Directory.CreateDirectory(directory);

        // Prefix with a GUID so concurrent uploads of the same filename cannot collide.
        var storedName = $"{Guid.NewGuid():N}_{safeFileName}";
        var fullPath = Path.Combine(directory, storedName);

        await using (var target = File.Create(fullPath))
        {
            await content.CopyToAsync(target, ct).ConfigureAwait(false);
        }

        return $"{safeContainer}/{storedName}";
    }

    public Task<Stream?> OpenAsync(string storageReference, CancellationToken ct = default)
    {
        var path = ResolveWithinRoot(storageReference);

        Stream? stream = path is not null && File.Exists(path) ? File.OpenRead(path) : null;

        return Task.FromResult(stream);
    }

    public Task<bool> DeleteAsync(string storageReference, CancellationToken ct = default)
    {
        var path = ResolveWithinRoot(storageReference);

        if (path is null || !File.Exists(path))
        {
            return Task.FromResult(false);
        }

        File.Delete(path);
        return Task.FromResult(true);
    }

    /// <summary>
    /// Resolves a reference to an absolute path, returning null if it escapes the root.
    /// </summary>
    /// <remarks>
    /// Storage references reaching this method may be attacker-influenced once uploads
    /// exist, so traversal is checked here rather than trusted from the caller.
    /// </remarks>
    private string? ResolveWithinRoot(string storageReference)
    {
        var combined = Path.GetFullPath(Path.Combine(_rootPath, storageReference));

        var rootWithSeparator = _rootPath.EndsWith(Path.DirectorySeparatorChar)
            ? _rootPath
            : _rootPath + Path.DirectorySeparatorChar;

        return combined.StartsWith(rootWithSeparator, StringComparison.Ordinal) ? combined : null;
    }

    private static string Sanitize(string value)
    {
        var cleaned = Path.GetFileName(value);

        return string.IsNullOrWhiteSpace(cleaned)
            ? throw new ArgumentException("Value does not contain a usable file or container name.", nameof(value))
            : cleaned;
    }
}
