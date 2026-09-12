using System.Text.Json;
using LoanManagement.Loans.Domain;
using LoanManagement.Shared.Kernel;
using Microsoft.EntityFrameworkCore;

namespace LoanManagement.Loans.Infrastructure.Persistence;

/// <summary>
/// EF Core DbContext for the Loans (LSS) service. Owns <c>LoansDb</c>.
///
/// The overridden <see cref="SaveChangesAsync"/> implements the write side of the
/// outbox pattern: any domain events raised by aggregates are serialized into the
/// <c>outbox_messages</c> table in the SAME transaction as the state change, so the
/// state and the "event to publish" record commit atomically (no dual-write risk).
/// </summary>
public sealed class LoansDbContext(
    DbContextOptions<LoansDbContext> options,
    ICorrelationIdAccessor? correlationIdAccessor = null)
    : DbContext(options)
{
    public DbSet<Loan> Loans => Set<Loan>();
    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();
    public DbSet<ProcessedEvent> ProcessedEvents => Set<ProcessedEvent>();

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(LoansDbContext).Assembly);
    }

    public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        DrainDomainEventsToOutbox();
        return await base.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// Collects domain events from tracked aggregates, writes them as outbox rows, and
    /// clears them. Runs inside the same SaveChanges transaction as the state change.
    /// </summary>
    private void DrainDomainEventsToOutbox()
    {
        var aggregates = ChangeTracker
            .Entries<AggregateRoot<LoanId>>()
            .Select(e => e.Entity)
            .Where(a => a.DomainEvents.Count > 0)
            .ToList();

        var correlationId = correlationIdAccessor?.CorrelationId;

        foreach (var aggregate in aggregates)
        {
            foreach (var domainEvent in aggregate.DomainEvents)
            {
                OutboxMessages.Add(new OutboxMessage
                {
                    Id = domainEvent.EventId,
                    Type = domainEvent.GetType().AssemblyQualifiedName!,
                    Payload = JsonSerializer.Serialize(domainEvent, domainEvent.GetType(), SerializerOptions),
                    CorrelationId = correlationId,
                    OccurredAt = domainEvent.OccurredAt,
                });
            }

            aggregate.ClearDomainEvents();
        }
    }
}
