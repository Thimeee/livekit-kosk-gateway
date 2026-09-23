using FastEndpoints;
using LivekitServerAPI.Contracts;
using LivekitServerAPI.Domain.Auth;
using LivekitServerAPI.Domain.Sessions;
using LivekitServerAPI.Infrastructure.LiveKit;
using Microsoft.Extensions.Options;

namespace LivekitServerAPI.Features.Tokens;

/// <summary>
/// Issues a LiveKit join token for a kiosk, teller or supervisor.
/// </summary>
/// <remarks>
/// <b>This endpoint is the entire authorisation boundary of the system.</b> LiveKit trusts any
/// token signed with the shared secret, so whatever is decided here is what the media server
/// enforces. Staff only - a kiosk gets its token from <c>POST /api/sessions</c> instead, bound
/// to the session it just created.
/// </remarks>
public class GenerateTokenEndpoint : Endpoint<TokenRequest, ApiResponse<TokenResponse>>
{
    private readonly ILiveKitTokenService _tokenService;
    private readonly LiveKitOptions _options;

    public GenerateTokenEndpoint(ILiveKitTokenService tokenService, IOptions<LiveKitOptions> options)
    {
        _tokenService = tokenService;
        _options = options.Value;
    }

    public override void Configure()
    {
        Post("/api/token");
        Policies(VtmPolicies.Staff);
    }

    public override async Task HandleAsync(TokenRequest req, CancellationToken ct)
    {
        // The validator has already confirmed this parses.
        var role = Enum.Parse<ParticipantRole>(req.Role, ignoreCase: true);

        var token = _tokenService.CreateJoinToken(role, req.RoomName, req.ParticipantName);
        var identity = $"{role.ToString().ToLowerInvariant()}-{req.ParticipantName}";

        Logger.LogInformation(
            "Token issued: {Role} {Identity} for room {RoomName}",
            role, identity, req.RoomName);

        var response = ApiResponse<TokenResponse>.Ok(
            new TokenResponse(token, identity, DateTimeOffset.UtcNow.Add(_options.TokenTtl)),
            "Token generated successfully.");

        await Send.OkAsync(response, cancellation: ct);
    }
}
