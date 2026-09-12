using System.Text.Json;
using LoanManagement.Origination.Domain;
using LoanManagement.Shared.Kernel;
using Microsoft.EntityFrameworkCore;

namespace LoanManagement.Origination.Infrastructure.Persistence;

/// <summary>
/// EF Core DbContext for the Origination (LOS) service. Owns <c>OriginationDb</c>.
///
/// <see cref="SaveChangesAsync"/> implements the outbox write side: domain events raised
/// by aggregates are captured into <c>outbox_messages</c> in the SAME transaction as the
/// state change. This is what makes submission "accept-and-queue": once SaveChanges commits,
/// the application AND its <c>LoanApplicationSubmitted</c> event are durable, and downstream
/// decisioning happens asynchronously — so a Credit/Contracts outage never blocks submission.
/// </summary>
public sealed class OriginationDbContext(DbContextOptions<OriginationDbContext> options)
    : DbContext(options)
{
    public DbSet<LoanApplication> Applications => Set<LoanApplication>();
    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();
    public DbSet<ProcessedEvent> ProcessedEvents => Set<ProcessedEvent>();

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(OriginationDbContext).Assembly);
    }

    public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        DrainDomainEventsToOutbox();
        return await base.SaveChangesAsync(cancellationToken);
    }

    private void DrainDomainEventsToOutbox()
    {
        var aggregates = ChangeTracker
            .Entries<AggregateRoot<Domain.ApplicationId>>()
            .Select(e => e.Entity)
            .Where(a => a.DomainEvents.Count > 0)
            .ToList();

        foreach (var aggregate in aggregates)
        {
            foreach (var domainEvent in aggregate.DomainEvents)
            {
                OutboxMessages.Add(new OutboxMessage
                {
                    Id = domainEvent.EventId,
                    Type = domainEvent.GetType().AssemblyQualifiedName!,
                    Payload = JsonSerializer.Serialize(domainEvent, domainEvent.GetType(), SerializerOptions),
                    OccurredAt = domainEvent.OccurredAt,
                });
            }

            aggregate.ClearDomainEvents();
        }
    }
}
