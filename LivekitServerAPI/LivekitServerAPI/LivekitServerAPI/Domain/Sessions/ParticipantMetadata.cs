namespace LivekitServerAPI.Domain.Sessions;

/// <summary>
/// Metadata attached to a participant's token and visible to everyone in the room.
/// Kept small on purpose - it travels inside the JWT.
/// </summary>
public sealed record ParticipantMetadata(string Role);
