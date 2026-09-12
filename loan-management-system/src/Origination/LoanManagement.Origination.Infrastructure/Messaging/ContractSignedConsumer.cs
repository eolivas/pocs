using LoanManagement.Origination.Application.IntegrationEvents;
using LoanManagement.Origination.Infrastructure.Persistence;
using LoanManagement.Shared.Kernel;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using ApplicationId = LoanManagement.Origination.Domain.ApplicationId;

namespace LoanManagement.Origination.Infrastructure.Messaging;

/// <summary>Advances an application's local status to Contracted when the contract is signed. Idempotent.</summary>
public sealed class ContractSignedConsumer(OriginationDbContext db, IDeduplicationStore dedup)
    : IConsumer<ContractSignedIntegrationEvent>
{
    private const string ConsumerName = "Origination.ContractSigned";

    public async Task Consume(ConsumeContext<ContractSignedIntegrationEvent> context)
    {
        var e = context.Message;
        if (await dedup.HasBeenProcessedAsync(e.EventId, ConsumerName, context.CancellationToken))
            return;

        var application = await db.Applications
            .FirstOrDefaultAsync(a => a.Id == new ApplicationId(e.ApplicationId), context.CancellationToken);

        if (application is not null)
        {
            application.MarkContracted();
            await dedup.MarkProcessedAsync(e.EventId, ConsumerName, context.CancellationToken);
            await db.SaveChangesAsync(context.CancellationToken);
        }
    }
}
