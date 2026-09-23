using System.Text.Json;
using Livekit.Server.Sdk.Dotnet;
using LivekitServerAPI.Domain.Sessions;
using Microsoft.Extensions.Options;

namespace LivekitServerAPI.Infrastructure.LiveKit;

/// <inheritdoc cref="ILiveKitTokenService"/>
public sealed class LiveKitTokenService : ILiveKitTokenService
{
    private readonly LiveKitOptions _options;
    private readonly ILogger<LiveKitTokenService> _logger;

    public LiveKitTokenService(IOptions<LiveKitOptions> options, ILogger<LiveKitTokenService> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    public string CreateJoinToken(ParticipantRole role, string roomName, string participantId)
    {
        var identity = $"{role.ToString().ToLowerInvariant()}-{participantId}";
        var metadata = JsonSerializer.Serialize(
            new ParticipantMetadata(role.ToString()),
            AppJsonSerializerContext.Default.ParticipantMetadata);

        var token = new AccessToken(_options.ApiKey, _options.ApiSecret)
            .WithIdentity(identity)
            .WithName(participantId)
            .WithMetadata(metadata)
            .WithTtl(_options.TokenTtl)
            .WithGrants(GrantsFor(role, roomName));

        _logger.LogInformation(
            "Issued {Role} token for {Identity} in {RoomName}, valid {TtlMinutes} minutes",
            role, identity, roomName, _options.TokenTtl.TotalMinutes);

        return token.ToJwt();
    }

    /// <summary>
    /// The single place where role maps to authority. Keep it that way - every authorisation
    /// decision in the system is ultimately expressed here.
    /// </summary>
    private static VideoGrants GrantsFor(ParticipantRole role, string roomName) => role switch
    {
        // Customer side. Joins, talks, shares its screen. Nothing else.
        ParticipantRole.Kiosk => new VideoGrants
        {
            RoomJoin = true,
            Room = roomName,
            CanPublish = true,
            CanSubscribe = true,
            CanPublishData = true,
            CanPublishSources = { "camera", "microphone", "screen_share" },
        },

        // Agent side. May moderate THIS room only - no RoomCreate, no RoomRecord.
        // Creating rooms and starting recordings are API-side operations, not browser ones.
        ParticipantRole.Teller => new VideoGrants
        {
            RoomJoin = true,
            Room = roomName,
            RoomAdmin = true,
            CanPublish = true,
            CanSubscribe = true,
            CanPublishData = true,
            CanPublishSources = { "camera", "microphone", "screen_share" },
        },

        // Observes without appearing in the room or publishing anything.
        ParticipantRole.Supervisor => new VideoGrants
        {
            RoomJoin = true,
            Room = roomName,
            Hidden = true,
            CanSubscribe = true,
            CanPublish = false,
            CanPublishData = false,
        },

        _ => throw new ArgumentOutOfRangeException(nameof(role), role, "Unknown participant role"),
    };
}
