namespace SchoolAdmission.Application.Common;

/// <summary>
/// Unified API response envelope ensuring consistent structure across all endpoints.
/// Prevents raw, inconsistent JSON output.
/// </summary>
public record ApiResponse<T>
{
    public bool Success { get; init; }
    public string Message { get; init; } = string.Empty;
    public T? Data { get; init; }
    public IReadOnlyList<string> Errors { get; init; } = Array.Empty<string>();
    public DateTime TimestampUtc { get; init; } = DateTime.UtcNow;

    public static ApiResponse<T> Ok(T data, string message = "Request processed successfully.") =>
        new()
        {
            Success = true,
            Message = message,
            Data = data
        };

    public static ApiResponse<T> Fail(string message, IEnumerable<string>? errors = null) =>
        new()
        {
            Success = false,
            Message = message,
            Errors = errors?.ToList().AsReadOnly() ?? (IReadOnlyList<string>)Array.Empty<string>()
        };
}
