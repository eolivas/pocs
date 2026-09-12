using LoanManagement.Shared.Kernel;

namespace LoanManagement.Origination.Domain.Events;

/// <summary>
/// An application has been accepted by Origination. Published via the outbox, it triggers
/// downstream decisioning (Credit Engine) asynchronously — so submission is never blocked
/// by a downstream outage. Carries IDs + the requested terms and the partner channel.
/// </summary>
public sealed record LoanApplicationSubmittedEvent(
    Guid ApplicationId,
    Guid PartnerId,
    string PartnerName,
    decimal RequestedAmount,
    string Currency,
    int TermMonths) : DomainEvent;
