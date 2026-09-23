namespace LivekitServerAPI.Domain.Sessions;

/// <summary>
/// Room metadata for a VTM session. Travels to every participant, so it stays small
/// and carries nothing sensitive.
/// </summary>
public sealed record SessionMetadata(
    string Status,
    string KioskId,
    string? BranchId,
    DateTimeOffset CreatedAt);
