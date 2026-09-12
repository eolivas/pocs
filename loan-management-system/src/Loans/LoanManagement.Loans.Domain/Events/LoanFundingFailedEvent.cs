using LoanManagement.Shared.Kernel;

namespace LoanManagement.Loans.Domain.Events;

/// <summary>
/// Funding or boarding could not be completed. Emitted as part of the saga's
/// compensation path so upstream services (Origination, Contracts) can react.
/// </summary>
public sealed record LoanFundingFailedEvent(
    Guid LoanId,
    Guid ContractId,
    Guid ApplicationId,
    string Reason) : DomainEvent;
