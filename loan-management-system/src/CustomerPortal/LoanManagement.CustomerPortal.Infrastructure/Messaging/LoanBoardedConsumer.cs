using LoanManagement.CustomerPortal.Application.IntegrationEvents;
using LoanManagement.CustomerPortal.Infrastructure.Projections;
using MassTransit;

namespace LoanManagement.CustomerPortal.Infrastructure.Messaging;

/// <summary>Projects <see cref="LoanBoardedIntegrationEvent"/> into the local BorrowerLoanView. Idempotent, out-of-order tolerant.</summary>
public sealed class LoanBoardedConsumer(BorrowerLoanProjection projection)
    : IConsumer<LoanBoardedIntegrationEvent>
{
    private const string ConsumerName = "Portal.LoanBoarded";

    public Task Consume(ConsumeContext<LoanBoardedIntegrationEvent> context)
        => projection.ApplyBoardedAsync(context.Message, ConsumerName, context.CancellationToken);
}
