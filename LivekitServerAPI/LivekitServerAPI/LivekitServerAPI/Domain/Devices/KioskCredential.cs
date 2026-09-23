namespace LivekitServerAPI.Domain.Devices;

/// <summary>
/// A kiosk's device credential. Separate from <see cref="Kiosk"/> so that rotating or revoking
/// one is its own operation rather than an edit to the device row, and so history survives.
/// </summary>
public class KioskCredential
{
    public Guid Id { get; set; }
    public string KioskId { get; set; } = string.Empty;

    /// <summary>Hashed. The plaintext secret is shown once at enrollment and never stored.</summary>
    public string SecretHash { get; set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? ExpiresAt { get; set; }

    /// <summary>Revoked rather than deleted, so an audit can still explain a past session.</summary>
    public DateTimeOffset? RevokedAt { get; set; }

    public Kiosk? Kiosk { get; set; }
}
