namespace LoanManagement.Origination.Infrastructure.Persistence;

/// <summary>Deduplication record (EventId + Consumer) for idempotent event consumption.</summary>
public sealed class ProcessedEvent
{
    public required Guid EventId { get; init; }
    public required string Consumer { get; init; }
    public DateTime ProcessedAt { get; init; } = DateTime.UtcNow;
}
