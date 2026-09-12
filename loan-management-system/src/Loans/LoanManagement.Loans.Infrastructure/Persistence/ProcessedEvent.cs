namespace LoanManagement.Loans.Infrastructure.Persistence;

/// <summary>
/// Deduplication record: marks that a given event has already been handled by a given
/// consumer. The composite key (EventId, Consumer) lets multiple consumers each process
/// the same event once. Backs <see cref="LoanManagement.Shared.Kernel.IDeduplicationStore"/>.
/// </summary>
public sealed class ProcessedEvent
{
    public required Guid EventId { get; init; }
    public required string Consumer { get; init; }
    public DateTime ProcessedAt { get; init; } = DateTime.UtcNow;
}
