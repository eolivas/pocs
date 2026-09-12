using LoanManagement.Shared.Kernel;
using Microsoft.EntityFrameworkCore;

namespace LoanManagement.CustomerPortal.Infrastructure.Persistence;

/// <summary>EF Core-backed deduplication store for idempotent projection consumers.</summary>
public sealed class DeduplicationStore(CustomerPortalDbContext db) : IDeduplicationStore
{
    public Task<bool> HasBeenProcessedAsync(Guid eventId, string consumer, CancellationToken cancellationToken = default)
        => db.ProcessedEvents.AsNoTracking()
            .AnyAsync(e => e.EventId == eventId && e.Consumer == consumer, cancellationToken);

    public Task MarkProcessedAsync(Guid eventId, string consumer, CancellationToken cancellationToken = default)
    {
        db.ProcessedEvents.Add(new ProcessedEvent { EventId = eventId, Consumer = consumer });
        return Task.CompletedTask;
    }
}
