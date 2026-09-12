using LoanManagement.CustomerPortal.Domain;

namespace LoanManagement.CustomerPortal.Application.DTOs;

/// <summary>Borrower-facing read model of a loan, served from the local projection.</summary>
public sealed record BorrowerLoanDto(
    Guid LoanId,
    Guid ContractId,
    Guid ApplicationId,
    string Status,
    decimal Amount,
    string Currency,
    DateTime? FundedAt,
    DateTime? BoardedAt,
    DateTime UpdatedAt)
{
    public static BorrowerLoanDto From(BorrowerLoanView v)
        => new(v.LoanId, v.ContractId, v.ApplicationId, v.Status, v.Amount, v.Currency, v.FundedAt, v.BoardedAt, v.UpdatedAt);
}
