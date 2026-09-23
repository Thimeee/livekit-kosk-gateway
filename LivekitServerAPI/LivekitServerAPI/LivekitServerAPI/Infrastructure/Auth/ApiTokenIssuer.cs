using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using LivekitServerAPI.Domain.Auth;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace LivekitServerAPI.Infrastructure.Auth;

/// <summary>
/// Issues the tokens callers present to <b>this API</b>.
/// </summary>
/// <remarks>
/// Not <see cref="LiveKit.ILiveKitTokenService"/>, which mints tokens for the media server. An API
/// token says who is calling; it is the <i>input</i> to deciding which LiveKit token they may have.
/// <para>
/// Only used under <see cref="AuthMode.Local"/>. Under OIDC the provider issues tokens and this
/// type is never resolved.
/// </para>
/// </remarks>
public interface IApiTokenIssuer
{
    (string Token, DateTimeOffset ExpiresAt) IssueStaffToken(VtmIdentity identity);

    (string Token, DateTimeOffset ExpiresAt) IssueKioskToken(VtmIdentity identity);
}

public sealed class JwtApiTokenIssuer : IApiTokenIssuer
{
    private readonly LocalAuthOptions _options;

    public JwtApiTokenIssuer(IOptions<AuthOptions> options) => _options = options.Value.Local;

    public (string Token, DateTimeOffset ExpiresAt) IssueStaffToken(VtmIdentity identity) =>
        Issue(identity, _options.AccessTokenTtl, kioskId: null);

    public (string Token, DateTimeOffset ExpiresAt) IssueKioskToken(VtmIdentity identity) =>
        Issue(identity, _options.KioskTokenTtl, kioskId: identity.SubjectId);

    private (string, DateTimeOffset) Issue(VtmIdentity identity, TimeSpan ttl, string? kioskId)
    {
        var now = DateTimeOffset.UtcNow;
        var expires = now.Add(ttl);

        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, identity.SubjectId),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString("N")),
            new(VtmClaims.Role, identity.Role),
            new(VtmClaims.Branch, identity.BranchId),
            new(VtmClaims.DisplayName, identity.DisplayName),
        };

        if (kioskId is not null)
        {
            claims.Add(new Claim(VtmClaims.KioskId, kioskId));
        }

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_options.SigningKey));
        var token = new JwtSecurityToken(
            issuer: _options.Issuer,
            audience: _options.Audience,
            claims: claims,
            notBefore: now.UtcDateTime,
            expires: expires.UtcDateTime,
            signingCredentials: new SigningCredentials(key, SecurityAlgorithms.HmacSha256));

        return (new JwtSecurityTokenHandler().WriteToken(token), expires);
    }
}
