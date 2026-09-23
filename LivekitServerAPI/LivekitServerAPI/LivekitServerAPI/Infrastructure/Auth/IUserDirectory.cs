namespace LivekitServerAPI.Infrastructure.Auth;

/// <summary>Who a caller turned out to be, once their credentials checked out.</summary>
public record VtmIdentity(string SubjectId, string DisplayName, string Role, string BranchId);

/// <summary>
/// Checks a username and password. <b>The only place the identity source appears.</b>
/// </summary>
/// <remarks>
/// Swapping a local table for on-prem AD is swapping this implementation - an LDAP bind instead
/// of a hash comparison. Everything above it, including the token that gets issued, is unchanged.
/// Under <see cref="AuthMode.Oidc"/> nothing implements this: the provider issues tokens directly
/// and there is no password for this API to see.
/// </remarks>
public interface IUserDirectory
{
    Task<VtmIdentity?> AuthenticateAsync(string username, string password, CancellationToken ct = default);
}

/// <summary>Checks a kiosk device secret. Same idea, different kind of caller.</summary>
public interface IKioskDirectory
{
    Task<VtmIdentity?> AuthenticateAsync(string kioskId, string secret, CancellationToken ct = default);
}
