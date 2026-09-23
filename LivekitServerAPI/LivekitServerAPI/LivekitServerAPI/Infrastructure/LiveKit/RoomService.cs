using System.Text.Json;
using Livekit.Server.Sdk.Dotnet;
using LivekitServerAPI.Domain.Sessions;
using Microsoft.Extensions.Options;

namespace LivekitServerAPI.Infrastructure.LiveKit;

/// <inheritdoc cref="IRoomService"/>
public sealed class RoomService : IRoomService
{
    /// <summary>Named <see cref="HttpClient"/> so the handler is pooled by the factory.</summary>
    public const string HttpClientName = "livekit-room";

    private const string RoomNamePrefix = "vtm-";

    private readonly RoomServiceClient _client;
    private readonly LiveKitOptions _options;
    private readonly ILogger<RoomService> _logger;

    public RoomService(
        IHttpClientFactory httpClientFactory,
        IOptions<LiveKitOptions> options,
        ILogger<RoomService> logger)
    {
        _options = options.Value;
        _logger = logger;

        // One client per scope (= per request). The SDK sets BaseAddress and mutates the
        // Authorization header on every call, so this instance must not be shared. See K-4.
        _client = new RoomServiceClient(
            _options.ServerUrl,
            _options.ApiKey,
            _options.ApiSecret,
            httpClientFactory.CreateClient(HttpClientName));
    }

    public async Task<IReadOnlyList<Room>> ListRoomsAsync(CancellationToken ct = default)
    {
        var response = await _client.ListRooms(new ListRoomsRequest());
        _logger.LogDebug("Listed {RoomCount} active rooms", response.Rooms.Count);
        return response.Rooms;
    }

    public async Task<Room> CreateSessionAsync(
        SessionMetadata metadata, CancellationToken ct = default)
    {
        // Generated, not caller-supplied: an unguessable name means a client cannot
        // ask for a token to a room it was never invited to.
        var roomName = RoomNamePrefix + Guid.NewGuid().ToString("N")[..12];

        var room = await _client.CreateRoom(new CreateRoomRequest
        {
            Name = roomName,
            MaxParticipants = _options.MaxSessionParticipants,
            EmptyTimeout = (uint)_options.SessionEmptyTimeout.TotalSeconds,
            DepartureTimeout = (uint)_options.SessionDepartureTimeout.TotalSeconds,
            Metadata = Serialize(metadata),
        });

        _logger.LogInformation(
            "Created session room {RoomName} (sid {RoomSid}) for kiosk {KioskId}, max {MaxParticipants} participants",
            room.Name, room.Sid, metadata.KioskId, _options.MaxSessionParticipants);

        return room;
    }

    public async Task<Room?> GetRoomAsync(string roomName, CancellationToken ct = default)
    {
        var response = await _client.ListRooms(new ListRoomsRequest { Names = { roomName } });
        return response.Rooms.Count > 0 ? response.Rooms[0] : null;
    }

    public async Task<IReadOnlyList<ParticipantInfo>> ListParticipantsAsync(
        string roomName, CancellationToken ct = default)
    {
        var response = await _client.ListParticipants(
            new ListParticipantsRequest { Room = roomName });
        return response.Participants;
    }

    public async Task UpdateSessionMetadataAsync(
        string roomName, SessionMetadata metadata, CancellationToken ct = default)
    {
        await _client.UpdateRoomMetadata(new UpdateRoomMetadataRequest
        {
            Room = roomName,
            Metadata = Serialize(metadata),
        });

        _logger.LogInformation(
            "Session {RoomName} metadata updated, status now {SessionStatus}",
            roomName, metadata.Status);
    }

    public async Task DeleteRoomAsync(string roomName, CancellationToken ct = default)
    {
        await _client.DeleteRoom(new DeleteRoomRequest { Room = roomName });
        _logger.LogInformation("Deleted session room {RoomName}", roomName);
    }

    // ── In-call control ─────────────────────────────────────────────────

    public async Task<ParticipantInfo?> GetParticipantAsync(
        string roomName, string identity, CancellationToken ct = default)
    {
        try
        {
            return await _client.GetParticipant(
                new RoomParticipantIdentity { Room = roomName, Identity = identity });
        }
        catch (Twirp.Exception ex) when (ex.Type == Twirp.ErrorCode.Not_Found)
        {
            // Absent rather than broken: the participant has already left, or never joined.
            return null;
        }
    }

    public async Task MuteTrackAsync(
        string roomName, string identity, string trackSid, bool muted,
        CancellationToken ct = default)
    {
        await _client.MutePublishedTrack(new MuteRoomTrackRequest
        {
            Room = roomName,
            Identity = identity,
            TrackSid = trackSid,
            Muted = muted,
        });

        _logger.LogInformation(
            "{Action} track {TrackSid} of {Identity} in {RoomName}",
            muted ? "Muted" : "Unmuted", trackSid, identity, roomName);
    }

    public async Task RemoveParticipantAsync(
        string roomName, string identity, CancellationToken ct = default)
    {
        await _client.RemoveParticipant(
            new RoomParticipantIdentity { Room = roomName, Identity = identity });

        _logger.LogInformation("Removed {Identity} from {RoomName}", identity, roomName);
    }

    public async Task UpdateParticipantPermissionsAsync(
        string roomName, string identity, bool canPublish, bool canPublishData,
        CancellationToken ct = default)
    {
        var participant = await GetParticipantAsync(roomName, identity, ct)
            ?? throw new Twirp.Exception(
                Twirp.ErrorCode.Not_Found, $"Participant '{identity}' is not in '{roomName}'.");

        // Permission is replaced wholesale, so start from what they already have.
        // Building a fresh ParticipantPermission would silently clear CanSubscribe,
        // Hidden and CanPublishSources.
        var permission = participant.Permission.Clone();
        permission.CanPublish = canPublish;
        permission.CanPublishData = canPublishData;

        await _client.UpdateParticipant(new UpdateParticipantRequest
        {
            Room = roomName,
            Identity = identity,
            Permission = permission,
        });

        _logger.LogInformation(
            "Updated {Identity} in {RoomName}: canPublish={CanPublish}, canPublishData={CanPublishData}",
            identity, roomName, canPublish, canPublishData);
    }

    public async Task SendDataAsync(
        string roomName, string topic, string payload, IReadOnlyList<string>? toIdentities,
        CancellationToken ct = default)
    {
        var request = new SendDataRequest
        {
            Room = roomName,
            Data = Google.Protobuf.ByteString.CopyFromUtf8(payload),
            Kind = DataPacket.Types.Kind.Reliable,
            Topic = topic,
        };

        if (toIdentities is { Count: > 0 })
        {
            request.DestinationIdentities.AddRange(toIdentities);
        }

        await _client.SendData(request);

        _logger.LogInformation(
            "Sent data on topic {Topic} to {Recipients} in {RoomName}",
            topic, toIdentities is { Count: > 0 } ? string.Join(",", toIdentities) : "everyone",
            roomName);
    }

    private static string Serialize(SessionMetadata metadata) =>
        JsonSerializer.Serialize(metadata, AppJsonSerializerContext.Default.SessionMetadata);
}
