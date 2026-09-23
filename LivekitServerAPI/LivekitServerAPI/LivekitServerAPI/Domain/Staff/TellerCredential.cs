namespace LivekitServerAPI.Domain.Staff;

/// <summary>
/// A teller password, for <c>Auth:Mode = "Local"</c> only.
/// </summary>
/// <remarks>
/// Mirrors <c>KioskCredential</c> so rotation and revocation are operations rather than edits.
/// If the bank moves to AD or an OIDC provider this table simply stops being read - the login
/// endpoint is bypassed entirely, and no code has to be removed.
/// </remarks>
public class TellerCredential
{
    public Guid Id { get; set; }
    public string TellerId { get; set; } = string.Empty;

    /// <summary>PBKDF2 via <c>PasswordHasher</c>. Never the password itself.</summary>
    public string PasswordHash { get; set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? RevokedAt { get; set; }

    /// <summary>Set on a successful login, so a dormant account is visible.</summary>
    public DateTimeOffset? LastUsedAt { get; set; }

    public Teller? Teller { get; set; }
}
