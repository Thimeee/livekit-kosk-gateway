namespace LivekitServerAPI.Domain.Staff;

public enum TellerStatus
{
    Offline,
    Available,
    Busy,
    Away,
}

/// <summary>
/// The VTM's view of a teller — <b>not</b> the bank's HR record and not the source of truth for
/// identity. The identity provider owns who they are; this row holds only what VTM routing needs.
/// Created or refreshed on first login.
/// </summary>
public class Teller
{
    /// <summary>Matches the <c>sub</c> claim from the identity provider.</summary>
    public string TellerId { get; set; } = string.Empty;

    public string DisplayName { get; set; } = string.Empty;
    public string BranchId { get; set; } = string.Empty;

    /// <summary>
    /// One of <see cref="Domain.Auth.VtmRoles"/> - teller, supervisor or admin. Held as a string
    /// rather than an enum because under an external IdP this is populated by mapping the
    /// provider's groups, and an unmapped value should be storable and visible, not a cast error.
    /// </summary>
    public string Role { get; set; } = Domain.Auth.VtmRoles.Teller;
    public TellerStatus Status { get; set; } = TellerStatus.Offline;

    /// <summary>How many sessions they may hold at once. Normally 1.</summary>
    public int MaxConcurrent { get; set; } = 1;

    public DateTimeOffset? LastSeenAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    public ICollection<TellerSkill> Skills { get; set; } = [];
}
