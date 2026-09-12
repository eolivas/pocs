using LoanManagement.Loans.Application.DTOs;
using LoanManagement.Loans.Domain;
using MediatR;

namespace LoanManagement.Loans.Application.Queries;

public sealed class GetLoanStatusHandler(ILoanRepository repository)
    : IRequestHandler<GetLoanStatusQuery, LoanStatusDto?>
{
    public async Task<LoanStatusDto?> Handle(GetLoanStatusQuery request, CancellationToken cancellationToken)
    {
        var loan = await repository.GetByContractIdAsync(new ContractId(request.ContractId), cancellationToken);
        return LoanStatusDto.From(loan);
    }
}
