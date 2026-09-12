using LoanManagement.Shared.Kernel;

namespace LoanManagement.Loans.Domain.Events;

/// <summary>
/// Funds have been disbursed for a signed contract. This is the pivot from applicant
/// to borrower. Carries IDs + the funded amount; consumers query back for more.
/// </summary>
public sealed record LoanFundedEvent(
    Guid LoanId,
    Guid ContractId,
    Guid ApplicationId,
    decimal Amount,
    string Currency) : DomainEvent;
