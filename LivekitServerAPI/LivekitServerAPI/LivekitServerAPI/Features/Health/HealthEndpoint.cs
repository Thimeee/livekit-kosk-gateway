using System.Diagnostics;
using FastEndpoints;
using LivekitServerAPI.Contracts;
using LivekitServerAPI.Infrastructure.LiveKit;

namespace LivekitServerAPI.Features.Health;

public record HealthResponse(string Status, string LiveKit, int ActiveRooms, long CheckMs);

/// <summary>
/// Liveness plus a real dependency check - it actually calls the LiveKit server.
/// Returns 503 when LiveKit is unreachable so a monitor or NSSM can act on it.
/// </summary>
public class HealthEndpoint : EndpointWithoutRequest<ApiResponse<HealthResponse>>
{
    private readonly IRoomService _roomService;

    public HealthEndpoint(IRoomService roomService) => _roomService = roomService;

    public override void Configure()
    {
        Get("/health");
        AllowAnonymous();
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();

        try
        {
            var rooms = await _roomService.ListRoomsAsync(ct);
            sw.Stop();

            await Send.OkAsync(
                ApiResponse<HealthResponse>.Ok(
                    new HealthResponse("healthy", "up", rooms.Count, sw.ElapsedMilliseconds)),
                cancellation: ct);
        }
        catch (Exception ex)
        {
            sw.Stop();
            Logger.LogError(ex, "Health check failed: LiveKit server unreachable");

            // Handled here rather than by LiveKitExceptionHandler - a health check should
            // report the failure as its payload, not surface it as an error envelope.
            await Send.ResponseAsync(
                new ApiResponse<HealthResponse>
                {
                    Success = false,
                    Status = StatusCodes.Status503ServiceUnavailable,
                    Message = "LiveKit server is unreachable.",
                    Data = new HealthResponse("degraded", "down", 0, sw.ElapsedMilliseconds),
                },
                StatusCodes.Status503ServiceUnavailable,
                cancellation: ct);
        }
    }
}
