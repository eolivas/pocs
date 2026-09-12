using LoanManagement.Shared.Kernel;
using Microsoft.EntityFrameworkCore;

namespace LoanManagement.Loans.Infrastructure.Persistence;

/// <summary>
/// EF Core-backed deduplication store. Records processed (event, consumer) pairs so a
/// consumer can skip an event it has already handled — making at-least-once delivery
/// effectively exactly-once. The unique composite key also protects against races:
/// a concurrent duplicate insert fails, which the consumer treats as "already processed".
/// </summary>
public sealed class DeduplicationStore(LoansDbContext db) : IDeduplicationStore
{
    public Task<bool> HasBeenProcessedAsync(Guid eventId, string consumer, CancellationToken cancellationToken = default)
        => db.ProcessedEvents.AsNoTracking()
            .AnyAsync(e => e.EventId == eventId && e.Consumer == consumer, cancellationToken);

    public Task MarkProcessedAsync(Guid eventId, string consumer, CancellationToken cancellationToken = default)
    {
        db.ProcessedEvents.Add(new ProcessedEvent { EventId = eventId, Consumer = consumer });
        // Persisted together with the consumer's own state change in one SaveChanges.
        return Task.CompletedTask;
    }
}
