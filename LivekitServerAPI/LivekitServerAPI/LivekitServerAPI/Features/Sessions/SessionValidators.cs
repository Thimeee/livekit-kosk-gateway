using FastEndpoints;
using FluentValidation;
using LivekitServerAPI.Domain.Sessions;

namespace LivekitServerAPI.Features.Sessions;

public class CreateSessionValidator : Validator<CreateSessionRequest>
{
    public CreateSessionValidator()
    {
        RuleFor(x => x.KioskId)
            .Cascade(CascadeMode.Stop)
            .NotEmpty().WithMessage("Kiosk id is required.")
            .MaximumLength(64).WithMessage("Kiosk id must be 64 characters or fewer.");

    }
}

public class UpdateSessionStatusValidator : Validator<UpdateSessionStatusRequest>
{
    public UpdateSessionStatusValidator()
    {
        RuleFor(x => x.Status)
            .Cascade(CascadeMode.Stop)
            .NotEmpty().WithMessage("Status is required.")
            .Must(s => Enum.TryParse<SessionStatus>(s, ignoreCase: true, out _))
            .WithMessage("Status must be one of: waiting, active, ending.");
    }
}
