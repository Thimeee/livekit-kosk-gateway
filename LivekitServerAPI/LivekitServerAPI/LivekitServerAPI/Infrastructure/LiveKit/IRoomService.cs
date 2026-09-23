using Livekit.Server.Sdk.Dotnet;
using LivekitServerAPI.Domain.Sessions;

namespace LivekitServerAPI.Infrastructure.LiveKit;

/// <summary>
/// Room and participant management against the LiveKit server.
/// </summary>
/// <remarks>
/// <b>Scoped, never singleton.</b> The underlying <see cref="RoomServiceClient"/> mutates
/// <c>HttpClient.DefaultRequestHeaders.Authorization</c> before every call, so a shared instance
/// lets concurrent requests send each other's tokens. See docs/PROJECT.md K-4.
/// </remarks>
public interface IRoomService
{
    /// <summary>Lists active rooms. Also used as the liveness probe for the LiveKit server.</summary>
    Task<IReadOnlyList<Room>> ListRoomsAsync(CancellationToken ct = default);

    /// <summary>
    /// Creates a session room. The name is generated here, never supplied by the caller -
    /// a client that chooses its own room name can join somebody else's session.
    /// </summary>
    Task<Room> CreateSessionAsync(SessionMetadata metadata, CancellationToken ct = default);

    /// <summary>Returns the room, or <c>null</c> if no active room has that name.</summary>
    Task<Room?> GetRoomAsync(string roomName, CancellationToken ct = default);

    /// <summary>Lists everyone currently in the room.</summary>
    Task<IReadOnlyList<ParticipantInfo>> ListParticipantsAsync(
        string roomName, CancellationToken ct = default);

    /// <summary>Replaces the room's metadata; broadcasts <c>RoomMetadataChanged</c> to everyone in it.</summary>
    Task UpdateSessionMetadataAsync(
        string roomName, SessionMetadata metadata, CancellationToken ct = default);

    /// <summary>Destroys the room and disconnects everyone. Idempotent.</summary>
    Task DeleteRoomAsync(string roomName, CancellationToken ct = default);

    // ── In-call control ─────────────────────────────────────────────────

    /// <summary>Returns the participant, or <c>null</c> if they are not in the room.</summary>
    Task<ParticipantInfo?> GetParticipantAsync(
        string roomName, string identity, CancellationToken ct = default);

    /// <summary>Mutes or unmutes one published track, server-side.</summary>
    Task MuteTrackAsync(
        string roomName, string identity, string trackSid, bool muted,
        CancellationToken ct = default);

    /// <summary>
    /// Disconnects a participant. They can rejoin with the same token unless it has
    /// expired, so pair this with a short TTL rather than relying on it alone.
    /// </summary>
    Task RemoveParticipantAsync(
        string roomName, string identity, CancellationToken ct = default);

    /// <summary>
    /// Changes what a participant may do, without reissuing their token. Used to put a
    /// customer into a read-only state mid-session.
    /// </summary>
    Task UpdateParticipantPermissionsAsync(
        string roomName, string identity, bool canPublish, bool canPublishData,
        CancellationToken ct = default);

    /// <summary>
    /// Server-originated data message. Fire and forget - use RPC when the result matters.
    /// </summary>
    Task SendDataAsync(
        string roomName, string topic, string payload, IReadOnlyList<string>? toIdentities,
        CancellationToken ct = default);

}
