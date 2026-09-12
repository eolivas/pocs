namespace LoanManagement.Shared.Kernel;

/// <summary>
/// Base class for aggregate roots. Aggregates are the only entities that raise
/// domain events. Events are collected here and drained (into the outbox) after the
/// aggregate is persisted, in the same transaction.
/// </summary>
public abstract class AggregateRoot<TId> : Entity<TId>
    where TId : struct
{
    private readonly List<IDomainEvent> _domainEvents = [];

    public IReadOnlyList<IDomainEvent> DomainEvents => _domainEvents.AsReadOnly();

    protected void RaiseDomainEvent(IDomainEvent domainEvent) => _domainEvents.Add(domainEvent);

    public void ClearDomainEvents() => _domainEvents.Clear();
}
