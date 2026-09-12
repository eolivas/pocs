using LoanManagement.CustomerPortal.Application.IntegrationEvents;
using LoanManagement.CustomerPortal.Infrastructure.Projections;
using MassTransit;

namespace LoanManagement.CustomerPortal.Infrastructure.Messaging;

/// <summary>Projects <see cref="LoanFundedIntegrationEvent"/> into the local BorrowerLoanView. Idempotent.</summary>
public sealed class LoanFundedConsumer(BorrowerLoanProjection projection)
    : IConsumer<LoanFundedIntegrationEvent>
{
    private const string ConsumerName = "Portal.LoanFunded";

    public Task Consume(ConsumeContext<LoanFundedIntegrationEvent> context)
        => projection.ApplyFundedAsync(context.Message, ConsumerName, context.CancellationToken);
}
