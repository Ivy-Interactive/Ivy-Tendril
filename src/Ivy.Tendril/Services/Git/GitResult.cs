namespace Ivy.Tendril.Services.Git;

public enum GitError
{
    GitNotFound,
    InvalidRepoPath,
    CommandFailed,
    Timeout,
    UnknownError
}

public enum DirtyReason
{
    NotOnExpectedBranch,
    AheadOfOrigin,
    DetachedHead,
    UncommittedChanges,
    UntrackedFiles,
    InProgressOperation,
    NoRemoteConfigured
}

public record DirtyReasonDetail
{
    public DirtyReason Reason { get; init; }
    public string Message { get; init; } = "";
    public List<string> Files { get; init; } = new();
}

public record DirtyRepoResult
{
    public bool IsDirty => Reasons.Count > 0;
    public List<DirtyReasonDetail> Reasons { get; init; } = new();
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
