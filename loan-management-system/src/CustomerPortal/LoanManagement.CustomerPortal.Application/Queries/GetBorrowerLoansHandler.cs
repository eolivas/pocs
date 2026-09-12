using LoanManagement.CustomerPortal.Application.DTOs;
using LoanManagement.CustomerPortal.Application.Interfaces;
using MediatR;

namespace LoanManagement.CustomerPortal.Application.Queries;

public sealed class GetBorrowerLoansHandler(IBorrowerLoanReadStore readStore)
    : IRequestHandler<GetBorrowerLoansQuery, IReadOnlyList<BorrowerLoanDto>>
{
    public Task<IReadOnlyList<BorrowerLoanDto>> Handle(GetBorrowerLoansQuery request, CancellationToken cancellationToken)
        => readStore.GetByApplicationAsync(request.ApplicationId, cancellationToken);
}
