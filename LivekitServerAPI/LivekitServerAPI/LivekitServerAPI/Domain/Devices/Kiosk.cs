namespace LivekitServerAPI.Domain.Devices;

public enum KioskStatus
{
    /// <summary>Registered but not yet enrolled — it has no credential and cannot connect.</summary>
    Provisioned,
    Active,
    Suspended,
    Retired,
}

/// <summary>A physical kiosk. Sessions can only be started by one that exists and is Active.</summary>
public class Kiosk
{
    public string KioskId { get; set; } = string.Empty;
    public string BranchId { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public KioskStatus Status { get; set; } = KioskStatus.Provisioned;
    public DateTimeOffset? EnrolledAt { get; set; }

    /// <summary>Last time this kiosk called the API. Drives an "offline kiosk" alert.</summary>
    public DateTimeOffset? LastSeenAt { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    public ICollection<KioskCredential> Credentials { get; set; } = [];
}
