using System.Text.Json;
using LivekitServerAPI.Contracts;
using Microsoft.AspNetCore.Diagnostics;

namespace LivekitServerAPI.Infrastructure.Errors;

/// <summary>
/// Turns failures from the LiveKit server into sensible HTTP responses instead of a bare 500.
/// </summary>
/// <remarks>
/// The SDK throws <c>Twirp.Exception</c> (global namespace) for server-side errors and
/// <see cref="HttpRequestException"/> when the server cannot be reached at all. The second case
/// maps to 503 so a kiosk can retry rather than showing a hard error to a customer.
/// </remarks>
public sealed class LiveKitExceptionHandler : IExceptionHandler
{
    private readonly ILogger<LiveKitExceptionHandler> _logger;

    public LiveKitExceptionHandler(ILogger<LiveKitExceptionHandler> logger) => _logger = logger;

    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        var (status, message) = exception switch
        {
            Twirp.Exception twirp => (MapTwirp(twirp.Type), twirp.Message),
            HttpRequestException => (StatusCodes.Status503ServiceUnavailable,
                                     "The LiveKit server is unreachable."),
            _ => (0, string.Empty),
        };

        // Not ours - let the default pipeline deal with it.
        if (status == 0)
        {
            return false;
        }

        _logger.LogError(
            exception,
            "LiveKit call failed on {Method} {Path}, responding {StatusCode}",
            httpContext.Request.Method, httpContext.Request.Path, status);

        httpContext.Response.StatusCode = status;
        httpContext.Response.ContentType = "application/json";

        var body = new ErrorResponseDto
        {
            Success = false,
            Status = status,
            Message = message,
        };

        await httpContext.Response.WriteAsync(
            JsonSerializer.Serialize(body, AppJsonSerializerContext.Default.ErrorResponseDto),
            cancellationToken);

        return true;
    }

    private static int MapTwirp(Twirp.ErrorCode code) => code switch
    {
        Twirp.ErrorCode.Not_Found => StatusCodes.Status404NotFound,
        Twirp.ErrorCode.Already_Exists => StatusCodes.Status409Conflict,
        Twirp.ErrorCode.Permission_Denied => StatusCodes.Status403Forbidden,
        Twirp.ErrorCode.Unauthenticated => StatusCodes.Status401Unauthorized,
        Twirp.ErrorCode.Invalid_Argument => StatusCodes.Status400BadRequest,
        Twirp.ErrorCode.Malformed => StatusCodes.Status400BadRequest,
        Twirp.ErrorCode.Out_Of_Range => StatusCodes.Status400BadRequest,
        Twirp.ErrorCode.Failed_Precondition => StatusCodes.Status412PreconditionFailed,
        Twirp.ErrorCode.Resource_Exhausted => StatusCodes.Status429TooManyRequests,
        Twirp.ErrorCode.Deadline_Exceeded => StatusCodes.Status504GatewayTimeout,
        Twirp.ErrorCode.Unavailable => StatusCodes.Status503ServiceUnavailable,
        Twirp.ErrorCode.Unimplemented => StatusCodes.Status501NotImplemented,
        _ => StatusCodes.Status502BadGateway,
    };
}
