using LoanManagement.Loans.Application.DTOs;
using MediatR;

namespace LoanManagement.Loans.Application.Queries;

/// <summary>Reads a loan's funding status by the contract it funds.</summary>
public sealed record GetLoanStatusQuery(Guid ContractId) : IRequest<LoanStatusDto?>;
