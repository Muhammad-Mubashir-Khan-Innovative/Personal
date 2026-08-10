namespace CarDealer.Application.Abstractions;

/// <summary>
/// The authenticated principal for the current request.
/// </summary>
public interface ICurrentUser
{
    bool IsAuthenticated { get; }

    long? UserId { get; }

    string? Email { get; }

    /// <summary>
    /// Permission codes granted in the active tenant only (acceptance criterion E6).
    /// </summary>
    IReadOnlySet<string> Permissions { get; }

    bool HasPermission(string permissionCode);
}
