using FastEndpoints;
using FluentValidation;
using LivekitServerAPI.Contracts;
using LivekitServerAPI.Infrastructure.Auth;

namespace LivekitServerAPI.Features.Auth;

public record LoginRequest(string Username, string Password);

public record KioskAuthRequest(string KioskId, string Secret);

public record AuthTokenResponse(
    string AccessToken,
    string TokenType,
    string SubjectId,
    string Role,
    string BranchId,
    DateTimeOffset ExpiresAt);

public class LoginValidator : Validator<LoginRequest>
{
    public LoginValidator()
    {
        RuleFor(x => x.Username).NotEmpty().WithMessage("Username is required.");
        RuleFor(x => x.Password).NotEmpty().WithMessage("Password is required.");
    }
}

public class KioskAuthValidator : Validator<KioskAuthRequest>
{
    public KioskAuthValidator()
    {
        RuleFor(x => x.KioskId).NotEmpty().WithMessage("Kiosk id is required.");
        RuleFor(x => x.Secret).NotEmpty().WithMessage("Secret is required.");
    }
}

/// <summary>
/// Exchanges a username and password for an API access token.
/// </summary>
/// <remarks>
/// Present only under <c>Auth:Mode = "Local"</c>. Under OIDC the provider issues tokens and this
/// endpoint is never reached - <c>IUserDirectory</c> is not registered, so it returns 501.
/// </remarks>
public class LoginEndpoint : Endpoint<LoginRequest, ApiResponse<AuthTokenResponse>>
{
    private readonly IServiceProvider _services;

    public LoginEndpoint(IServiceProvider services) => _services = services;

    public override void Configure()
    {
        Post("/api/auth/login");
        AllowAnonymous(); // The one endpoint that must be, by definition.
    }

    public override async Task HandleAsync(LoginRequest req, CancellationToken ct)
    {
        var directory = _services.GetService<IUserDirectory>();
        var issuer = _services.GetService<IApiTokenIssuer>();

        if (directory is null || issuer is null)
        {
            await Send.ResponseAsync(
                new ApiResponse<AuthTokenResponse>
                {
                    Success = false,
                    Status = StatusCodes.Status501NotImplemented,
                    Message = "This deployment authenticates through an external provider. "
                              + "Obtain a token from it instead.",
                },
                StatusCodes.Status501NotImplemented,
                cancellation: ct);
            return;
        }

        var identity = await directory.AuthenticateAsync(req.Username, req.Password, ct);
        if (identity is null)
        {
            // Deliberately vague: never reveal whether the username exists.
            await Send.ResponseAsync(
                new ApiResponse<AuthTokenResponse>
                {
                    Success = false,
                    Status = StatusCodes.Status401Unauthorized,
                    Message = "Invalid username or password.",
                },
                StatusCodes.Status401Unauthorized,
                cancellation: ct);
            return;
        }

        var (token, expiresAt) = issuer.IssueStaffToken(identity);

        await Send.OkAsync(
            ApiResponse<AuthTokenResponse>.Ok(
                new AuthTokenResponse(
                    token, "Bearer", identity.SubjectId, identity.Role, identity.BranchId, expiresAt),
                "Signed in."),
            cancellation: ct);
    }
}

/// <summary>
/// Exchanges a kiosk device secret for a short-lived API token.
/// </summary>
/// <remarks>
/// The branch and kiosk id on the token come from the enrolled record, so a kiosk cannot claim
/// to be a different device or to be in a different branch.
/// </remarks>
public class KioskAuthEndpoint : Endpoint<KioskAuthRequest, ApiResponse<AuthTokenResponse>>
{
    private readonly IServiceProvider _services;

    public KioskAuthEndpoint(IServiceProvider services) => _services = services;

    public override void Configure()
    {
        Post("/api/auth/kiosk");
        AllowAnonymous();
    }

    public override async Task HandleAsync(KioskAuthRequest req, CancellationToken ct)
    {
        var directory = _services.GetService<IKioskDirectory>();
        var issuer = _services.GetService<IApiTokenIssuer>();

        if (directory is null || issuer is null)
        {
            await Send.ResponseAsync(
                new ApiResponse<AuthTokenResponse>
                {
                    Success = false,
                    Status = StatusCodes.Status501NotImplemented,
                    Message = "Device authentication is not enabled in this deployment.",
                },
                StatusCodes.Status501NotImplemented,
                cancellation: ct);
            return;
        }

        var identity = await directory.AuthenticateAsync(req.KioskId, req.Secret, ct);
        if (identity is null)
        {
            await Send.ResponseAsync(
                new ApiResponse<AuthTokenResponse>
                {
                    Success = false,
                    Status = StatusCodes.Status401Unauthorized,
                    Message = "Invalid kiosk credentials.",
                },
                StatusCodes.Status401Unauthorized,
                cancellation: ct);
            return;
        }

        var (token, expiresAt) = issuer.IssueKioskToken(identity);

        Logger.LogInformation(
            "Kiosk {KioskId} authenticated for branch {BranchId}",
            identity.SubjectId, identity.BranchId);

        await Send.OkAsync(
            ApiResponse<AuthTokenResponse>.Ok(
                new AuthTokenResponse(
                    token, "Bearer", identity.SubjectId, identity.Role, identity.BranchId, expiresAt)),
            cancellation: ct);
    }
}
