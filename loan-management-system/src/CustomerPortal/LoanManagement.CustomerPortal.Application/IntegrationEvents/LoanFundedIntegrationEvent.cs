namespace LoanManagement.CustomerPortal.Application.IntegrationEvents;

/// <summary>
/// Inbound integration event: the Loans service reports that a loan was funded. The
/// Customer Portal consumes this to build its local <c>BorrowerLoanView</c> projection.
///
/// In production this contract is shared (or duplicated per consumer) between Loans and
/// the Portal. <see cref="EventId"/> drives idempotent, at-least-once-safe consumption.
/// </summary>
public sealed record LoanFundedIntegrationEvent(
    Guid EventId,
    Guid LoanId,
    Guid ContractId,
    Guid ApplicationId,
    decimal Amount,
    string Currency,
    DateTime FundedAt);
