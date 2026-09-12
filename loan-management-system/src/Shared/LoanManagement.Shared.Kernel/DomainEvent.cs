namespace LoanManagement.Shared.Kernel;

/// <summary>
/// Marker for a domain event — a fact about something that already happened.
/// Immutable; carries only IDs and minimal data. Past-tense naming.
/// </summary>
public interface IDomainEvent
{
    Guid EventId { get; }
    DateTime OccurredAt { get; }
}

/// <summary>
/// Base record for domain events. Each event gets a unique <see cref="EventId"/>
/// (used for idempotent, at-least-once-safe consumption) and an occurrence timestamp.
/// </summary>
public abstract record DomainEvent : IDomainEvent
{
    public Guid EventId { get; init; } = Guid.NewGuid();
    public DateTime OccurredAt { get; init; } = DateTime.UtcNow;
}
