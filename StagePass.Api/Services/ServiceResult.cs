namespace StagePass.Api.Services;

public record ServiceResult<T>(bool Success, T? Value, string? ErrorCode)
{
    public static ServiceResult<T> Ok(T value) => new(true, value, null);
    public static ServiceResult<T> Fail(string errorCode) => new(false, default, errorCode);
}

public record ServiceResult(bool Success, string? ErrorCode)
{
    public static ServiceResult Ok() => new(true, null);
    public static ServiceResult Fail(string errorCode) => new(false, errorCode);
}
