using System.Security.Claims;
using System.Text;
using LivekitServerAPI.Domain.Auth;
using LivekitServerAPI.Domain.Devices;
using LivekitServerAPI.Domain.Staff;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.IdentityModel.Tokens;

namespace LivekitServerAPI.Infrastructure.Auth;

/// <summary>
/// Wires authentication and authorisation. The <b>only</b> place the choice between a local user
/// table and an external provider is visible.
/// </summary>
public static class AuthenticationSetup
{
    public static IServiceCollection AddVtmAuthentication(
        this IServiceCollection services, IConfiguration configuration)
    {
        var section = configuration.GetSection(AuthOptions.SectionName);
        var options = section.Get<AuthOptions>() ?? new AuthOptions();

        services.AddOptions<AuthOptions>()
            .Bind(section)
            .Validate(
                o => o.Mode != AuthMode.Local
                     || Encoding.UTF8.GetByteCount(o.Local.SigningKey) >= 32,
                "Auth:Local:SigningKey must be at least 32 bytes when Auth:Mode is Local.")
            .Validate(
                o => o.Mode != AuthMode.Oidc || !string.IsNullOrWhiteSpace(o.Oidc.Authority),
                "Auth:Oidc:Authority is required when Auth:Mode is Oidc.")
            .ValidateOnStart();

        var authentication = services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme);

        if (options.Mode == AuthMode.Local)
        {
            authentication.AddJwtBearer(jwt =>
            {
                // Off, or the handler silently rewrites "role" and "sub" into the long
                // WS-Federation URIs and every RequireClaim(VtmClaims.Role, ...) fails with
                // a 403 that looks like a policy bug.
                jwt.MapInboundClaims = false;

                jwt.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidIssuer = options.Local.Issuer,
                    ValidateAudience = true,
                    ValidAudience = options.Local.Audience,
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = new SymmetricSecurityKey(
                        Encoding.UTF8.GetBytes(options.Local.SigningKey)),
                    ValidateLifetime = true,
                    ClockSkew = TimeSpan.FromSeconds(30),
                    RoleClaimType = VtmClaims.Role,
                    NameClaimType = VtmClaims.Subject,
                };

                // A browser cannot set an Authorization header on a WebSocket handshake, so
                // SignalR sends the token as a query parameter instead. Scoped to /hubs so a
                // token in a URL is never accepted for a normal API call, where it would end
                // up in access logs.
                jwt.Events = new JwtBearerEvents
                {
                    OnMessageReceived = context =>
                    {
                        var token = context.Request.Query["access_token"];
                        if (!string.IsNullOrEmpty(token)
                            && context.HttpContext.Request.Path.StartsWithSegments("/hubs"))
                        {
                            context.Token = token;
                        }

                        return Task.CompletedTask;
                    },
                };
            });

            services.AddScoped<IUserDirectory, SqlUserDirectory>();
            services.AddScoped<IKioskDirectory, SqlKioskDirectory>();
            services.AddSingleton<IApiTokenIssuer, JwtApiTokenIssuer>();
            services.AddSingleton<IPasswordHasher<Teller>, PasswordHasher<Teller>>();
            services.AddSingleton<IPasswordHasher<Kiosk>, PasswordHasher<Kiosk>>();
        }
        else
        {
            authentication.AddJwtBearer(jwt =>
            {
                jwt.MapInboundClaims = false;

                // Keys are fetched from the provider's JWKS and rotated automatically.
                jwt.Authority = options.Oidc.Authority;
                jwt.Audience = options.Oidc.Audience;
                jwt.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateLifetime = true,
                    ClockSkew = TimeSpan.FromSeconds(30),
                    RoleClaimType = VtmClaims.Role,
                    NameClaimType = VtmClaims.Subject,
                };

                // The provider's groups are mapped onto our roles here and nowhere else.
                // Endpoints never see an AD group name, which is what keeps the identity
                // source swappable.
                jwt.Events = new JwtBearerEvents
                {
                    OnTokenValidated = context =>
                    {
                        if (context.Principal?.Identity is not ClaimsIdentity id)
                        {
                            return Task.CompletedTask;
                        }

                        foreach (var (providerRole, vtmRole) in options.Oidc.RoleClaimMappings)
                        {
                            var held = id.HasClaim("groups", providerRole)
                                       || id.HasClaim(ClaimTypes.Role, providerRole)
                                       || id.HasClaim("roles", providerRole);

                            if (held && !id.HasClaim(VtmClaims.Role, vtmRole))
                            {
                                id.AddClaim(new Claim(VtmClaims.Role, vtmRole));
                            }
                        }

                        var branch = id.FindFirst(options.Oidc.BranchClaim)?.Value;
                        if (branch is not null && id.FindFirst(VtmClaims.Branch) is null)
                        {
                            id.AddClaim(new Claim(VtmClaims.Branch, branch));
                        }

                        return Task.CompletedTask;
                    },
                };
            });
        }

        services.AddAuthorization(auth =>
        {
            auth.AddPolicy(VtmPolicies.Kiosk, p => p.RequireClaim(VtmClaims.Role, VtmRoles.Kiosk));

            // A supervisor can do anything a teller can - they are covering the same desk.
            auth.AddPolicy(VtmPolicies.Teller, p =>
                p.RequireClaim(VtmClaims.Role, VtmRoles.Teller, VtmRoles.Supervisor));

            auth.AddPolicy(VtmPolicies.Supervisor, p =>
                p.RequireClaim(VtmClaims.Role, VtmRoles.Supervisor));

            auth.AddPolicy(VtmPolicies.Admin, p => p.RequireClaim(VtmClaims.Role, VtmRoles.Admin));

            auth.AddPolicy(VtmPolicies.Staff, p =>
                p.RequireClaim(VtmClaims.Role, VtmRoles.Teller, VtmRoles.Supervisor, VtmRoles.Admin));

            // Either side of a call can be dropped, so either side can ask to come back.
            // Listing two policies on an endpoint requires both of them, which is how the
            // rejoin endpoint came to refuse every kiosk.
            auth.AddPolicy(VtmPolicies.SessionParticipant, p =>
                p.RequireClaim(VtmClaims.Role,
                    VtmRoles.Kiosk, VtmRoles.Teller, VtmRoles.Supervisor, VtmRoles.Admin));
        });

        return services;
    }
}

/// <summary>Reads the current caller out of the claims principal.</summary>
public static class CallerExtensions
{
    public static string? SubjectId(this ClaimsPrincipal user) =>
        user.FindFirst(VtmClaims.Subject)?.Value
        ?? user.FindFirst(System.IdentityModel.Tokens.Jwt.JwtRegisteredClaimNames.Sub)?.Value;

    public static string? Role(this ClaimsPrincipal user) =>
        user.FindFirst(VtmClaims.Role)?.Value;

    public static string? Branch(this ClaimsPrincipal user) =>
        user.FindFirst(VtmClaims.Branch)?.Value;

    /// <summary>
    /// True when the caller may act on something belonging to <paramref name="branchId"/>.
    /// Admins and supervisors are not branch-bound; a teller is.
    /// </summary>
    public static bool CanActOnBranch(this ClaimsPrincipal user, string branchId)
    {
        var role = user.Role();
        if (role is VtmRoles.Admin or VtmRoles.Supervisor)
        {
            return true;
        }

        return string.Equals(user.Branch(), branchId, StringComparison.OrdinalIgnoreCase);
    }
}
