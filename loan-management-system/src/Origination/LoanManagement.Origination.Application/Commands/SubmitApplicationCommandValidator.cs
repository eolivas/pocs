using FluentValidation;

namespace LoanManagement.Origination.Application.Commands;

public sealed class SubmitApplicationCommandValidator : AbstractValidator<SubmitApplicationCommand>
{
    public SubmitApplicationCommandValidator()
    {
        RuleFor(x => x.PartnerId).NotEmpty();
        RuleFor(x => x.PartnerName).NotEmpty().MaximumLength(200);
        RuleFor(x => x.RequestedAmount).GreaterThan(0m);
        RuleFor(x => x.Currency).NotEmpty().Length(3);
        RuleFor(x => x.TermMonths).GreaterThan(0);
    }
}
