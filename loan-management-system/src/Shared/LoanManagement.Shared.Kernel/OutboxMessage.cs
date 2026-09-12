namespace LoanManagement.Shared.Kernel;

/// <summary>
/// A domain event captured in the outbox table, written in the SAME database
/// transaction as the aggregate state change (solving the dual-write problem).
/// A background processor later publishes it to the broker and marks it processed.
/// </summary>
public sealed class OutboxMessage
{
    public Guid Id { get; init; } = Guid.NewGuid();

    /// <summary>CLR type name of the event, used to deserialize <see cref="Payload"/>.</summary>
    public required string Type { get; init; }

    /// <summary>JSON-serialized event body.</summary>
    public required string Payload { get; init; }

    /// <summary>Correlation id propagated from the originating request through to consumers.</summary>
    public string? CorrelationId { get; init; }

    public DateTime OccurredAt { get; init; } = DateTime.UtcNow;

    /// <summary>Set when successfully published. Null = pending.</summary>
    public DateTime? ProcessedAt { get; set; }

    public int RetryCount { get; set; }

    /// <summary>Set when the message exhausted retries (dead-lettered).</summary>
    public DateTime? FailedAt { get; set; }

    public string? FailureReason { get; set; }
}
