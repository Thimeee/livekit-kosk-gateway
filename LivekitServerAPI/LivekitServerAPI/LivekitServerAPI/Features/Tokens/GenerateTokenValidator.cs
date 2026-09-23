using FastEndpoints;
using FluentValidation;
using LivekitServerAPI.Domain.Sessions;

namespace LivekitServerAPI.Features.Tokens;

public class GenerateTokenValidator : Validator<TokenRequest>
{
    public GenerateTokenValidator()
    {
        // Cascade.Stop: an empty value should report "required", not also "must be one of".
        RuleFor(x => x.Role)
            .Cascade(CascadeMode.Stop)
            .NotEmpty().WithMessage("Role is required.")
            .Must(role => Enum.TryParse<ParticipantRole>(role, ignoreCase: true, out _))
            .WithMessage("Role must be one of: kiosk, teller, supervisor.");

        RuleFor(x => x.RoomName)
            .Cascade(CascadeMode.Stop)
            .NotEmpty().WithMessage("Room name is required.")
            .MaximumLength(128).WithMessage("Room name must be 128 characters or fewer.");

        RuleFor(x => x.ParticipantName)
            .Cascade(CascadeMode.Stop)
            .NotEmpty().WithMessage("Participant name is required.")
            .MaximumLength(128).WithMessage("Participant name must be 128 characters or fewer.");
    }
}
