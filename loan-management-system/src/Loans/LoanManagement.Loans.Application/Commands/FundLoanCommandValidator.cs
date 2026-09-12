using FluentValidation;

namespace LoanManagement.Loans.Application.Commands;

public sealed class FundLoanCommandValidator : AbstractValidator<FundLoanCommand>
{
    public FundLoanCommandValidator()
    {
        RuleFor(x => x.ContractId).NotEmpty();
        RuleFor(x => x.ApplicationId).NotEmpty();
        RuleFor(x => x.Amount).GreaterThan(0m).WithMessage("Funding amount must be greater than zero.");
        RuleFor(x => x.Currency).NotEmpty().Length(3).WithMessage("Currency must be a 3-letter code.");
    }
}
