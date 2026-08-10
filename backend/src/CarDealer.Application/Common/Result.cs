namespace CarDealer.Application.Common;

/// <summary>
/// Outcome of an operation. Used instead of exceptions for expected failures, so that
/// "wrong password" and "database unreachable" cannot be confused for one another.
/// </summary>
public sealed class Result<T>
{
    private Result(bool succeeded, T? value, ErrorKind errorKind, string? error)
    {
        Succeeded = succeeded;
        Value = value;
        ErrorKind = errorKind;
        Error = error;
    }

    public bool Succeeded { get; }

    public T? Value { get; }

    public ErrorKind ErrorKind { get; }

    public string? Error { get; }

    public static Result<T> Success(T value) => new(true, value, ErrorKind.None, null);

    public static Result<T> Failure(ErrorKind kind, string error) => new(false, default, kind, error);
}

public enum ErrorKind
{
    None = 0,

    /// <summary>Input failed validation. Maps to 400.</summary>
    Validation = 1,

    /// <summary>Caller is not authenticated, or credentials were wrong. Maps to 401.</summary>
    Unauthenticated = 2,

    /// <summary>Caller is authenticated but lacks permission. Maps to 403 (criterion E3).</summary>
    Forbidden = 3,

    /// <summary>Target does not exist, or is invisible to this tenant. Maps to 404.</summary>
    NotFound = 4,

    /// <summary>Request conflicts with current state. Maps to 409.</summary>
    Conflict = 5,
}
