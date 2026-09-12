using LoanManagement.Loans.Application.IntegrationEvents;
using LoanManagement.Loans.Application.Interfaces;
using MediatR;

namespace LoanManagement.Loans.Application.Commands;

public sealed class FundLoanHandler(IFundingService funding) : IRequestHandler<FundLoanCommand>
{
    public Task Handle(FundLoanCommand request, CancellationToken cancellationToken)
        => funding.FundAsync(
            new ContractSignedIntegrationEvent(
                request.EventId, request.ContractId, request.ApplicationId, request.Amount, request.Currency),
            cancellationToken);
}
