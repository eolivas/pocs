using LoanManagement.Loans.Domain;

namespace LoanManagement.Loans.Application.DTOs;

/// <summary>Read model for a loan's funding status.</summary>
public sealed record LoanStatusDto(
    Guid LoanId,
    Guid ContractId,
    Guid ApplicationId,
    string Status,
    decimal Amount,
    string Currency,
    DateTime? FundedAt,
    DateTime? BoardedAt,
    string? FailureReason)
{
    public static LoanStatusDto? From(Loan? loan)
        => loan is null
            ? null
            : new LoanStatusDto(
                loan.Id.Value,
                loan.ContractId.Value,
                loan.ApplicationId.Value,
                loan.Status.ToString(),
                loan.Principal.Amount,
                loan.Principal.Currency,
                loan.FundedAt,
                loan.BoardedAt,
                loan.FailureReason);
}
