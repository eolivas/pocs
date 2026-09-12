namespace LoanManagement.Origination.Application.IntegrationEvents;

/// <summary>Inbound: Credit Engine reports a decision for an application. Advances local status.</summary>
public sealed record CreditDecisionMadeIntegrationEvent(
    Guid EventId,
    Guid ApplicationId,
    bool Approved);
