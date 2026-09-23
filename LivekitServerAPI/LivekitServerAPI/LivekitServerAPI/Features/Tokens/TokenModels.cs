namespace LivekitServerAPI.Features.Tokens;

/// <param name="Role">"kiosk", "teller" or "supervisor" - case-insensitive.</param>
/// <param name="RoomName">The session room to join.</param>
/// <param name="ParticipantName">Identifier for this participant within the room.</param>
public record TokenRequest(string Role, string RoomName, string ParticipantName);

public record TokenResponse(string Token, string Identity, DateTimeOffset ExpiresAt);
