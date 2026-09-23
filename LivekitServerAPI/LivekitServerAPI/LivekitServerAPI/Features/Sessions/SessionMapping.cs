using System.Text.Json;
using Livekit.Server.Sdk.Dotnet;
using LivekitServerAPI.Domain.Sessions;

namespace LivekitServerAPI.Features.Sessions;

/// <summary>
/// Turns LiveKit protobuf messages into the shapes this API returns.
/// </summary>
internal static class SessionMapping
{
    public static SessionSummary ToSummary(Room room)
    {
        var metadata = ReadMetadata(room.Metadata);

        return new SessionSummary(
            RoomName: room.Name,
            RoomSid: room.Sid,
            Status: metadata?.Status ?? SessionStatus.Waiting.ToString(),
            KioskId: metadata?.KioskId,
            BranchId: metadata?.BranchId,
            NumParticipants: room.NumParticipants,
            NumPublishers: room.NumPublishers,
            ActiveRecording: room.ActiveRecording,
            // LiveKit reports creation time as Unix seconds.
            CreatedAt: metadata?.CreatedAt ?? DateTimeOffset.FromUnixTimeSeconds(room.CreationTime));
    }

    public static ParticipantSummary ToSummary(ParticipantInfo participant) =>
        new(
            Identity: participant.Identity,
            Name: participant.Name,
            Sid: participant.Sid,
            Role: RoleFromIdentity(participant.Identity),
            State: participant.State.ToString(),
            IsPublisher: participant.IsPublisher,
            TrackCount: participant.Tracks.Count,
            JoinedAt: DateTimeOffset.FromUnixTimeSeconds(participant.JoinedAt));

    /// <summary>
    /// Identities are minted by <c>TokenService</c> as "{role}-{participantId}", so the
    /// prefix is reliable. Falls back to "unknown" for anything this API did not issue -
    /// an agent or an egress participant, for instance.
    /// </summary>
    private static string RoleFromIdentity(string identity)
    {
        var dash = identity.IndexOf('-');
        if (dash <= 0)
        {
            return "unknown";
        }

        var prefix = identity[..dash];
        return Enum.TryParse<ParticipantRole>(prefix, ignoreCase: true, out var role)
            ? role.ToString()
            : "unknown";
    }

    /// <summary>
    /// Room metadata is free-form text as far as LiveKit is concerned, and rooms can be
    /// created outside this API, so a parse failure is expected rather than exceptional.
    /// </summary>
    private static SessionMetadata? ReadMetadata(string? metadata)
    {
        if (string.IsNullOrWhiteSpace(metadata))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize(
                metadata, AppJsonSerializerContext.Default.SessionMetadata);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
