using System.ComponentModel.DataAnnotations;

namespace LivekitServerAPI.Infrastructure.Auth;

/// <summary>Where the tokens this API accepts come from.</summary>
public enum AuthMode
{
    /// <summary>This API authenticates users itself and signs its own tokens.</summary>
    Local,

    /// <summary>An external OIDC provider issues tokens; this API only validates them.</summary>
    Oidc,
}

/// <summary>
/// Bound from the "Auth" section. Switching <see cref="Mode"/> is the whole of moving between a
/// local user table, on-prem AD, and Entra - no endpoint changes.
/// </summary>
public sealed class AuthOptions
{
    public const string SectionName = "Auth";

    public AuthMode Mode { get; set; } = AuthMode.Local;

    public LocalAuthOptions Local { get; set; } = new();
    public OidcAuthOptions Oidc { get; set; } = new();

    /// <summary>
    /// Seeded on first run so there is someone who can register kiosks and create tellers.
    /// Leave the password empty in any environment that already has an admin.
    /// </summary>
    public BootstrapAdminOptions BootstrapAdmin { get; set; } = new();
}

public sealed class LocalAuthOptions
{
    /// <summary>Symmetric signing key. At least 32 bytes, same rule as the LiveKit secret.</summary>
    public string SigningKey { get; set; } = string.Empty;

    public string Issuer { get; set; } = "vtm-api";
    public string Audience { get; set; } = "vtm-api";

    /// <summary>
    /// Short by design. A teller console open all shift needs refresh tokens, not a long-lived
    /// access token that cannot be revoked.
    /// </summary>
    public TimeSpan AccessTokenTtl { get; set; } = TimeSpan.FromMinutes(30);

    /// <summary>Kiosks are unattended, so their tokens are shorter still.</summary>
    public TimeSpan KioskTokenTtl { get; set; } = TimeSpan.FromMinutes(15);
}

public sealed class OidcAuthOptions
{
    /// <summary>e.g. <c>https://login.microsoftonline.com/{tenant}/v2.0</c>. Keys come from its JWKS.</summary>
    public string Authority { get; set; } = string.Empty;

    public string Audience { get; set; } = string.Empty;

    /// <summary>
    /// Maps the provider's groups or roles onto <see cref="Domain.Auth.VtmRoles"/>. This is the
    /// only place an AD group name is allowed to appear.
    /// </summary>
    public Dictionary<string, string> RoleClaimMappings { get; set; } = [];

    /// <summary>Which claim carries the caller's branch, if the provider supplies one.</summary>
    public string BranchClaim { get; set; } = "branch";
}

public sealed class BootstrapAdminOptions
{
    public string TellerId { get; set; } = "admin";
    public string DisplayName { get; set; } = "Bootstrap Administrator";
    public string BranchId { get; set; } = "head-office";

    /// <summary>Empty disables seeding. Change it immediately after first login.</summary>
    public string Password { get; set; } = string.Empty;
}
