namespace LoanManagement.Origination.Application.IntegrationEvents;

/// <summary>Inbound: Contracts reports a signed contract for an application. Advances local status to Contracted.</summary>
public sealed record ContractSignedIntegrationEvent(
    Guid EventId,
    Guid ApplicationId);
