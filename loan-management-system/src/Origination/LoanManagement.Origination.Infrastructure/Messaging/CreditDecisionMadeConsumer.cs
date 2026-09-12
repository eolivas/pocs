using LoanManagement.Origination.Application.IntegrationEvents;
using LoanManagement.Origination.Infrastructure.Persistence;
using LoanManagement.Shared.Kernel;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using ApplicationId = LoanManagement.Origination.Domain.ApplicationId;

namespace LoanManagement.Origination.Infrastructure.Messaging;

/// <summary>
/// Advances an application's local status when Credit Engine reports a decision. Idempotent
/// (dedup) and updates only the local aggregate — this is the status-projection read side
/// that keeps the applicant's "where's my application?" view available.
/// </summary>
public sealed class CreditDecisionMadeConsumer(OriginationDbContext db, IDeduplicationStore dedup)
    : IConsumer<CreditDecisionMadeIntegrationEvent>
{
    private const string ConsumerName = "Origination.CreditDecisionMade";

    public async Task Consume(ConsumeContext<CreditDecisionMadeIntegrationEvent> context)
    {
        var e = context.Message;
        if (await dedup.HasBeenProcessedAsync(e.EventId, ConsumerName, context.CancellationToken))
            return;

        var application = await db.Applications
            .FirstOrDefaultAsync(a => a.Id == new ApplicationId(e.ApplicationId), context.CancellationToken);

        // Tolerate out-of-order: if the submitted event/aggregate isn't here yet, skip;
        // the outbox will redeliver (or a later replay will catch up).
        if (application is not null)
        {
            application.MarkDecisioned(e.Approved);
            await dedup.MarkProcessedAsync(e.EventId, ConsumerName, context.CancellationToken);
            await db.SaveChangesAsync(context.CancellationToken);
        }
    }
}
