namespace LoanManagement.CustomerPortal.Infrastructure.Persistence;

/// <summary>
/// Deduplication record: marks that a given event was already handled by a given consumer,
/// so at-least-once delivery becomes effectively exactly-once. Composite key (EventId, Consumer).
/// Backs <see cref="LoanManagement.Shared.Kernel.IDeduplicationStore"/>.
/// </summary>
public sealed class ProcessedEvent
{
    public required Guid EventId { get; init; }
    public required string Consumer { get; init; }
    public DateTime ProcessedAt { get; init; } = DateTime.UtcNow;
}
