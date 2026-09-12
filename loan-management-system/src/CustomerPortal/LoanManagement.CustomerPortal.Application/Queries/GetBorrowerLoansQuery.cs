using LoanManagement.CustomerPortal.Application.DTOs;
using MediatR;

namespace LoanManagement.CustomerPortal.Application.Queries;

/// <summary>Lists a borrower's loans from the local projection (by originating application).</summary>
public sealed record GetBorrowerLoansQuery(Guid ApplicationId) : IRequest<IReadOnlyList<BorrowerLoanDto>>;
