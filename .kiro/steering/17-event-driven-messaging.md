---
inclusion: fileMatch
fileMatchPattern: "**/*Consumer*.cs,**/*Event*.cs,**/*Saga*.cs,**/*Publisher*.cs"
---

# Event-Driven Development & Messaging Best Practices

This document covers event design, schema evolution, idempotency, ordering, error handling, and messaging patterns. For MassTransit-specific configuration and consumer scaffolding, see `04-masstransit-consumer-event-publishing.md`.

## Event Design Principles

### Events Are Facts, Not Commands

Events describe something that already happened. They are immutable records of state transitions.

| ✅ Event (past tense) | ❌ Command (imperative) |
|------------------------|-------------------------|
| `{Entity}PlacedEvent` | `Place{Entity}Command` |
| `PaymentProcessedEvent` | `ProcessPaymentCommand` |
| `UserRegisteredEvent` | `RegisterUserCommand` |

Rules:
- Name events as `{Aggregate}{PastTenseVerb}Event`
- Events are published by the owning service — consumers react to them
- Commands are sent to a specific handler — only one handler processes them
- Never put business logic in events — they carry data, not behaviour

### Event Payload Design

Events carry the minimum data needed by known consumers:

```csharp
// GOOD: Carries IDs and key facts — consumers query back if they need more
public sealed record {Entity}PlacedEvent({Entity}Id {Entity}Id, CustomerId CustomerId, DateTime PlacedAt) : DomainEvent;

// BAD: Carries the entire aggregate state (tight coupling, versioning nightmare)
public sealed record {Entity}PlacedEvent({Entity}Id Id, string Status, List<LineDto> Lines, MoneyDto Total, ...) : DomainEvent;
```

Rules:
- Include the aggregate ID (always needed for correlation)
- Include IDs of related entities that consumers need for their own queries
- Include the timestamp of when the event occurred
- Do NOT include data that only one specific consumer needs — let it query back
- Do NOT include sensitive data (PII, credentials) — use references instead

### Event Granularity

One event per meaningful business state transition:

```csharp
// GOOD: Separate events for separate transitions
public sealed record {Entity}CreatedEvent(...) : DomainEvent;  // Created in Pending
public sealed record {Entity}PlacedEvent(...) : DomainEvent;   // Pending → Placed
public sealed record {Entity}CancelledEvent(...) : DomainEvent; // → Cancelled
public sealed record {Entity}ShippedEvent(...) : DomainEvent;  // Placed → Shipped

// BAD: Generic event with a "type" discriminator
public sealed record {Entity}StatusChangedEvent(string OldStatus, string NewStatus, ...) : DomainEvent;
```

Rules:
- Each event represents one specific state transition
- Consumers subscribe to the transitions they care about — not all of them
- Avoid generic "changed" events — they force consumers to interpret the change

## Schema Evolution & Versioning

### Backward-Compatible Changes (Safe)

These changes do NOT break existing consumers:
- Adding a new optional field (consumers ignore unknown fields)
- Adding a new event type (existing consumers don't subscribe)
- Deprecating a field (stop populating, but keep in schema)

### Breaking Changes (Require Coordination)

These changes break consumers and need the expand-contract pattern:
- Removing a field that consumers depend on
- Renaming a field
- Changing a field's type
- Changing the event's namespace or assembly

### Expand-Contract Pattern for Breaking Changes

```
Phase 1 (Expand):  Publish both old and new event formats simultaneously
Phase 2 (Migrate): Consumers switch to new format one by one
Phase 3 (Contract): Stop publishing old format after all consumers migrated
```

Rules:
- Never make breaking changes to a published event schema in a single deploy
- Use the expand-contract pattern — minimum 2 releases
- Version events by namespace if major restructuring: `{SolutionName}.Domain.Events.V2.{Entity}PlacedEvent`
- Document all schema changes in the service's CHANGELOG

### Serialization

- Use `System.Text.Json` for event serialization (configured in outbox)
- Enable `JsonSerializerOptions.PropertyNameCaseInsensitive = true` for consumer-side deserialization
- Use `[JsonPropertyName]` for explicit field names if needed for cross-language consumers
- Never use `BinaryFormatter` or `Newtonsoft.Json` type-discriminated serialization (security risk)

## Idempotency

### Why Idempotency Matters

At-least-once delivery means consumers WILL receive duplicate events. Every consumer must handle the same event multiple times without side effects.

### Strategies

| Strategy | When to Use | Example |
|----------|-------------|---------|
| Natural idempotency | Operation is inherently safe to repeat | Setting a status to "Placed" (already Placed → no-op) |
| Deduplication table | Side effects are not naturally idempotent | Check `processed_events` table before sending email |
| Idempotency key | External API calls | Pass event ID as `Idempotency-Key` header to payment gateway |
| Upsert | Writing to a read model | `INSERT ... ON CONFLICT DO UPDATE` |

### Implementation Pattern

```csharp
public sealed class {Entity}PlacedConsumer : IConsumer<{Entity}PlacedEvent>
{
    private readonly IDeduplicationStore _dedup;

    public async Task Consume(ConsumeContext<{Entity}PlacedEvent> context)
    {
        var eventId = context.MessageId?.ToString() ?? context.Message.{Entity}Id.Value.ToString();

        if (await _dedup.HasBeenProcessedAsync(eventId))
            return; // Already handled — skip

        // Process the event...
        await SendConfirmationEmail(context.Message);

        await _dedup.MarkAsProcessedAsync(eventId);
    }
}
```

Rules:
- Every consumer MUST be idempotent — test with duplicate message delivery
- Use `MessageId` from the transport or a domain-specific key for deduplication
- Deduplication entries should have a TTL (e.g., 7 days) to prevent unbounded growth
- For database writes, prefer upserts over insert-then-check patterns

## Ordering Guarantees

### Default: No Ordering Guarantee

MassTransit with RabbitMQ/SNS/SQS does NOT guarantee message ordering. Messages may arrive out of order, especially during retries or redeliveries.

### When Ordering Matters

If your consumer depends on receiving events in sequence (e.g., Created before Placed before Shipped):

| Strategy | Trade-off |
|----------|-----------|
| Design for out-of-order delivery | Best throughput, most resilient — consumer handles any order |
| Sequence number in event payload | Consumer detects gaps and defers processing |
| Single-partition key (SQS FIFO, Service Bus sessions) | Guarantees order but limits throughput to one consumer per key |

### Recommended Approach: Design for Out-of-Order

```csharp
public async Task Consume(ConsumeContext<{Entity}ShippedEvent> context)
{
    var entity = await _readModel.GetAsync(context.Message.{Entity}Id);

    if (entity is null)
    {
        // Created event hasn't arrived yet — schedule retry
        throw new InvalidOperationException("Entity not found — will retry.");
    }

    entity.MarkAsShipped(context.Message.ShippedAt);
    await _readModel.SaveAsync(entity);
}
```

Rules:
- Design consumers to tolerate events arriving out of order
- Use retry (MassTransit's built-in exponential backoff) for "not ready yet" scenarios
- Never assume events arrive in the same order they were published
- If strict ordering is required, document the trade-off and use FIFO queues with partition keys

## Error Handling & Dead Letters

### Retry Strategy

```
Attempt 1: Immediate
Attempt 2: 1 second delay
Attempt 3: 4 seconds delay
Attempt 4: 8 seconds delay
→ Dead-letter queue after max retries
```

Configured via MassTransit consumer retry (see `04-masstransit-consumer-event-publishing.md`).

### Transient vs. Permanent Failures

| Failure Type | Example | Action |
|-------------|---------|--------|
| Transient | Database timeout, network blip, downstream 503 | Retry with backoff |
| Permanent | Invalid event payload, missing required data, business rule violation | Dead-letter immediately |

```csharp
public async Task Consume(ConsumeContext<{Entity}PlacedEvent> context)
{
    try
    {
        await ProcessEvent(context.Message);
    }
    catch (InvalidEventDataException ex)
    {
        // Permanent failure — don't retry, move to DLQ immediately
        _logger.LogError(ex, "Permanently failed to process {EventType}", nameof({Entity}PlacedEvent));
        throw; // MassTransit moves to _error queue after max retries
    }
    // Transient exceptions (DbException, HttpRequestException) are retried automatically
}
```

### Dead-Letter Queue (DLQ) Management

- Dead-lettered messages go to `{queue-name}_error` queues
- Monitor DLQ depth — alert when depth > 0
- Investigate root cause before replaying messages
- Replay workflow: fix the bug → redeploy → replay messages from DLQ
- Never auto-replay DLQ messages without human review

### Poison Message Detection

If a message repeatedly fails and is retried, it blocks other messages in the queue:

Rules:
- Max retry count is configured per consumer (default: 3 in MassTransit, 5 in outbox)
- After max retries, the message is moved to DLQ — it does NOT block other messages
- Log the full exception and message payload on final failure for debugging
- Include correlation ID in error logs for tracing back to the originating request

## Messaging Patterns

### Publish-Subscribe (Fan-Out)

One event, multiple consumers — each consumer gets its own copy:

```
{Entity} Service publishes {Entity}PlacedEvent
    → Notifications Service: sends confirmation email
    → Analytics Service: updates dashboard metrics
    → Inventory Service: reserves stock
```

Rules:
- Publisher has no knowledge of subscribers
- Each subscriber has its own queue (MassTransit auto-creates per consumer type)
- Adding a new subscriber does NOT require redeploying the publisher

### Request-Reply (Avoid in Most Cases)

Synchronous messaging via the broker — generally an anti-pattern:

Rules:
- Prefer HTTP for synchronous request-response
- Prefer async events for fire-and-forget workflows
- Only use request-reply when you need the transport guarantees of the broker AND synchronous response

### Saga / Process Manager

For long-running business processes spanning multiple events:

```
{Entity}PlacedEvent → Reserve Inventory → Process Payment → Ship {Entity}
```

Rules:
- Use MassTransit Sagas (state machines) for multi-step workflows
- Each step is triggered by an event and produces the next event
- Sagas are persisted (EF Core or Redis) to survive restarts
- Compensating actions handle failures (e.g., release inventory if payment fails)
- Only introduce sagas when the workflow genuinely spans multiple services

## Testing Event-Driven Code

### Unit Testing Consumers

```csharp
[Fact]
public async Task Consume_ValidEvent_SendsNotification()
{
    var consumer = new {Entity}PlacedConsumer(_notificationService, _dedup);
    var context = Mock.Of<ConsumeContext<{Entity}PlacedEvent>>(
        c => c.Message == new {Entity}PlacedEvent(...) && c.MessageId == Guid.NewGuid());

    await consumer.Consume(context);

    _notificationService.Verify(n => n.SendAsync(It.IsAny<Email>()), Times.Once());
}
```

### Integration Testing with MassTransit Test Harness

```csharp
[Fact]
public async Task {Entity}Placed_TriggersNotificationConsumer()
{
    await using var harness = new InMemoryTestHarness();
    var consumerHarness = harness.Consumer<{Entity}PlacedConsumer>();

    await harness.Start();
    await harness.Bus.Publish(new {Entity}PlacedEvent(...));

    Assert.True(await consumerHarness.Consumed.Any<{Entity}PlacedEvent>());
}
```

### Property-Based Testing for Idempotency

```csharp
[Property(MaxTest = 100)]
public Property Consumer_IsIdempotent_WhenEventDeliveredMultipleTimes()
{
    return Prop.ForAll(
        Arb.Default.Guid(),
        eventId =>
        {
            // Deliver same event 3 times
            // Assert: side effect occurs exactly once
        });
}
```

## Monitoring & Observability

### Key Metrics

| Metric | Alert Threshold | What It Means |
|--------|----------------|---------------|
| `outbox.messages.processed` | — | Events successfully published from outbox |
| `outbox.messages.failed` | > 0 | Events that exhausted retries (dead-lettered) |
| `outbox.message.duration_ms` | p99 > 5000ms | Outbox processing is slow |
| DLQ depth | > 0 | Messages requiring human investigation |
| Consumer lag | Growing over time | Consumers falling behind publishers |
| Message age (time since publish) | > 30s | End-to-end delivery taking too long |

### Correlation Across Boundaries

Every message carries `X-Correlation-Id` in its headers:
1. HTTP request starts the correlation chain
2. Outbox stores it alongside the event
3. OutboxProcessor includes it when publishing
4. Consumers extract it and push to log context

This allows tracing a single user action across: API request → domain event → outbox → broker → consumer processing.
