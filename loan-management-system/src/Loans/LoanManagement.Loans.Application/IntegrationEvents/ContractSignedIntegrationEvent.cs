namespace LoanManagement.Loans.Application.IntegrationEvents;

/// <summary>
/// Inbound integration event: the Contracts service reports that a contract was signed.
/// This is what triggers funding. It carries the IDs and the principal to fund.
///
/// In a production system this contract would be shared (or duplicated per consumer)
/// between Contracts and Loans; for the POC, Loans owns its inbound contract definition.
/// The <see cref="EventId"/> is used for idempotent, at-least-once-safe consumption.
/// </summary>
public sealed record ContractSignedIntegrationEvent(
    Guid EventId,
    Guid ContractId,
    Guid ApplicationId,
    decimal Amount,
    string Currency);
