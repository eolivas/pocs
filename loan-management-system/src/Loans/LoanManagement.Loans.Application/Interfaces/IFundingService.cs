using LoanManagement.Loans.Application.IntegrationEvents;

namespace LoanManagement.Loans.Application.Interfaces;

/// <summary>
/// Idempotent funding operations, shared by the message consumer / saga. Implementations
/// combine the deduplication store, the find-or-create-by-contract rule, and the Loan
/// aggregate's own idempotent transitions so that duplicate delivery funds exactly once.
/// </summary>
public interface IFundingService
{
    /// <summary>
    /// Funds the loan for a signed contract. Safe to call multiple times for the same
    /// contract/event — the loan is funded exactly once.
    /// </summary>
    Task FundAsync(ContractSignedIntegrationEvent @event, CancellationToken cancellationToken = default);

    /// <summary>Boards a funded loan. Idempotent.</summary>
    Task BoardAsync(Guid contractId, CancellationToken cancellationToken = default);
}
