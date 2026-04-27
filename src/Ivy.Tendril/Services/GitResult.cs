namespace Ivy.Tendril.Services;

public enum GitError
{
    GitNotFound,
    InvalidRepoPath,
    CommandFailed,
    Timeout,
    UnknownError
}

public class GitResult<T>
{
    public bool IsSuccess { get; }
    public T? Value { get; }
    public GitError? Error { get; }
    public string? ErrorMessage { get; }

    private GitResult(bool isSuccess, T? value, GitError? error, string? errorMessage)
    {
        IsSuccess = isSuccess;
        Value = value;
        Error = error;
        ErrorMessage = errorMessage;
    }

    public static GitResult<T> Success(T value) => new(true, value, null, null);

    public static GitResult<T> Failure(GitError error, string? message = null) =>
        new(false, default, error, message);

    public T GetValueOrDefault(T defaultValue = default!) => IsSuccess ? Value! : defaultValue;
}
