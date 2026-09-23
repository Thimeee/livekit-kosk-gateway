namespace LivekitServerAPI.Domain.Auth;

/// <summary>
/// The roles this API understands. <b>Ours, not the identity provider's.</b>
/// </summary>
/// <remarks>
/// When an external IdP is used, its groups are mapped onto these during claims transformation.
/// Endpoints must never see an AD group name or an IdP-specific role - that is what keeps the
/// identity source swappable.
/// </remarks>
public static class VtmRoles
{
    public const string Kiosk = "kiosk";
    public const string Teller = "teller";
    public const string Supervisor = "supervisor";
    public const string Admin = "admin";
}

/// <summary>Claim types in the tokens this API issues and accepts.</summary>
public static class VtmClaims
{
    /// <summary>Teller id, kiosk id, or admin id. Standard <c>sub</c>.</summary>
    public const string Subject = "sub";

    public const string Role = "role";

    /// <summary>Which branch the caller belongs to. Scopes what they may touch.</summary>
    public const string Branch = "branch";

    /// <summary>Present only on kiosk tokens.</summary>
    public const string KioskId = "kiosk_id";

    public const string DisplayName = "name";
}

/// <summary>Named authorisation policies. Endpoints reference these, never raw roles.</summary>
public static class VtmPolicies
{
    public const string Kiosk = "Kiosk";
    public const string Teller = "Teller";
    public const string Supervisor = "Supervisor";
    public const string Admin = "Admin";
    public const string Staff = "Staff";

    /// <summary>
    /// Anyone who can legitimately be in a session: a kiosk or any member of staff.
    /// </summary>
    /// <remarks>
    /// This exists because <c>Policies(Kiosk, Staff)</c> requires <b>both</b>, not either, and a
    /// kiosk is not staff - so every kiosk request was refused with 403. Found by calling the
    /// rejoin endpoint rather than by reading it.
    /// </remarks>
    public const string SessionParticipant = "SessionParticipant";
}
