namespace LivekitServerAPI.Contracts;

/// <summary>Standard success envelope returned by every endpoint.</summary>
public class ApiResponse<T>
{
    public bool Success { get; set; } = true;
    public int Status { get; set; }
    public string Message { get; set; } = "Success";
    public T? Data { get; set; }

    public static ApiResponse<T> Ok(T data, string message = "Success") =>
        new() { Success = true, Status = 200, Message = message, Data = data };
}

/// <summary>Standard failure envelope - validation failures and unhandled errors alike.</summary>
public class ErrorResponseDto
{
    public bool Success { get; set; }
    public int Status { get; set; }
    public string Message { get; set; } = string.Empty;
    public Dictionary<string, string[]> Errors { get; set; } = new();
}
