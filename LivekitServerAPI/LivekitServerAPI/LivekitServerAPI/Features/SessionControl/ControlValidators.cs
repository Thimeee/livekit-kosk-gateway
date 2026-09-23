using FastEndpoints;
using FluentValidation;

namespace LivekitServerAPI.Features.SessionControl;

public class NotifyValidator : Validator<NotifyRequest>
{
    public NotifyValidator()
    {
        RuleFor(x => x.Topic)
            .Cascade(CascadeMode.Stop)
            .NotEmpty().WithMessage("Topic is required.")
            .MaximumLength(64).WithMessage("Topic must be 64 characters or fewer.");

        // Data messages travel over the peer connection; anything large belongs in a
        // byte stream, not here.
        RuleFor(x => x.Payload)
            .Cascade(CascadeMode.Stop)
            .NotEmpty().WithMessage("Payload is required.")
            .MaximumLength(15_000).WithMessage("Payload must be 15000 characters or fewer.");
    }
}

public class TransferSessionValidator : Validator<TransferSessionRequest>
{
    public TransferSessionValidator()
    {
        RuleFor(x => x.ToTellerId)
            .Cascade(CascadeMode.Stop)
            .NotEmpty().WithMessage("Incoming teller id is required.")
            .MaximumLength(64).WithMessage("Teller id must be 64 characters or fewer.");
    }
}

public class MonitorSessionValidator : Validator<MonitorSessionRequest>
{
    public MonitorSessionValidator()
    {
        RuleFor(x => x.SupervisorId)
            .Cascade(CascadeMode.Stop)
            .NotEmpty().WithMessage("Supervisor id is required.")
            .MaximumLength(64).WithMessage("Supervisor id must be 64 characters or fewer.");
    }
}
