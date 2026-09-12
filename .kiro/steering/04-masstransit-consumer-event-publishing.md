---
inclusion: fileMatch
fileMatchPattern: "**/*Consumer*.cs,**/*Event*.cs,**/*IntegrationEvent*.cs"
---

# MassTransit Consumer & Event Publishing

This project uses MassTransit with an outbox pattern for reliable domain event delivery. All messaging infrastructure lives in `src/{SolutionName}.Infrastructure/Messaging/`.

## Architecture Overview

```
Domain Event raised in Aggregate
    ↓
SaveChangesAsync intercepts domain events
    ↓
Serialized to outbox_messages table (same DB transaction)
    ↓
OutboxProcessor (BackgroundService) polls on configurable interval
    ↓
Deserializes, publishes via MassTransit IPublishEndpoint (with correlation ID)
    ↓
Marks as processed (or increments RetryCount on failure)
```

## Transport Configuration

MassTransit transport is configured conditionally in `MessagingServiceCollectionExtensions.AddMessaging()`:

- **RabbitMQ** (when `RabbitMq:Host` is configured): Used in Docker Compose and production. Connects with exponential backoff startup retry (5 attempts). Consumer retry: 3 retries, exponential 1s→8s. Failed messages go to `_error` suffix dead-letter queue.
- **InMemory** (when `RabbitMq:Host` is absent): Fallback for local development without Docker. Logs a warning at startup.

```csharp
// In Program.cs — already wired via extension method:
builder.Services.AddMessaging(builder.Configuration);
```

Configuration section:
```json
{
  "RabbitMq": {
    "Host": "rabbitmq",
    "Username": "guest",
    "Password": "guest",
    "ConsumerRetryCount": 3,
    "StartupRetryAttempts": 5
  }
}
```

## Defining a New Domain Event

1. Create the event in `src/{SolutionName}.Domain/Events/`:

```csharp
using {SolutionName}.Domain.Common;

namespace {SolutionName}.Domain.Events;

public sealed record {Entity}ShippedEvent({Entity}Id {Entity}Id, DateTime ShippedAt) : DomainEvent;
```

2. Raise it from the aggregate:

```csharp
public void Ship()
{
    if (Status != {Entity}Status.Placed)
        throw new {Entity}DomainException("Only placed entities can be shipped.");

    Status = {Entity}Status.Shipped;
    RaiseDomainEvent(new {Entity}ShippedEvent(Id, DateTime.UtcNow));
}
```

## Publishing Events (Application Layer)

The handler publishes events after persisting:

```csharp
await _repo.SaveAsync(entity, cancellationToken);

foreach (var domainEvent in entity.DomainEvents)
{
    await _publisher.PublishAsync(domainEvent, cancellationToken);
}

entity.ClearDomainEvents();
```

The `IApplicationEventPublisher` interface lives in `Application/Interfaces/`. The implementation (`MassTransitEventPublisher`) lives in Infrastructure.

## Creating a New Consumer

Place in `src/{SolutionName}.Infrastructure/Messaging/`:

```csharp
using MassTransit;
using Microsoft.Extensions.Logging;
using {SolutionName}.Domain.Events;

namespace {SolutionName}.Infrastructure.Messaging;

public sealed class {Entity}ShippedConsumer : IConsumer<{Entity}ShippedEvent>
{
    private readonly ILogger<{Entity}ShippedConsumer> _logger;

    public {Entity}ShippedConsumer(ILogger<{Entity}ShippedConsumer> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public Task Consume(ConsumeContext<{Entity}ShippedEvent> context)
    {
        _logger.LogInformation(
            "Received {Entity}ShippedEvent for {Entity} {EntityId}",
            context.Message.{Entity}Id);

        // Process the event (send notification, update read model, etc.)
        return Task.CompletedTask;
    }
}
```

Rules:
- Class name: `{EventName without "Event"}Consumer` (e.g., `{Entity}ShippedConsumer`)
- `sealed class` implementing `IConsumer<TEvent>`
- Inject dependencies via constructor
- Use structured logging with message templates

## MassTransit Registration

In `Program.cs`, messaging is registered via the infrastructure extension method:

```csharp
builder.Services.AddMessaging(builder.Configuration);
```

This conditionally configures RabbitMQ or InMemory transport. Consumers are auto-discovered via assembly scanning and `ConfigureEndpoints`.

## Idempotency Expectations

- Consumers MUST be idempotent — the same event may be delivered more than once
- Use deduplication (check if already processed) or make operations naturally idempotent
- The outbox marks messages as processed after successful publish, but consumers should not assume exactly-once delivery

## Outbox Pattern

The `OutboxProcessor` background service:
- Polls `outbox_messages` table on configurable interval (default 5s, `Outbox:PollingIntervalSeconds`)
- Retrieves unprocessed messages in batches (default 20, `Outbox:BatchSize`) ordered by `OccurredAt`
- Query filter: `WHERE ProcessedAt IS NULL AND FailedAt IS NULL`
- Deserializes the event payload using `System.Text.Json`
- Publishes via MassTransit `IPublishEndpoint`, including `X-Correlation-Id` in message headers
- Marks `ProcessedAt` on success
- On failure: increments `RetryCount`. When `RetryCount >= MaxRetries` (default 5), sets `FailedAt` and `FailureReason` (dead-lettered)
- Emits OTEL metrics: `outbox.messages.processed`, `outbox.messages.failed`, `outbox.message.duration_ms`

### Outbox Retention

`OutboxRetentionService` runs periodically (default 60 min) and deletes processed messages older than the retention period (default 7 days) in batches (default 500).

Configuration:
```json
{
  "Outbox": {
    "BatchSize": 20,
    "MaxRetries": 5,
    "PollingIntervalSeconds": 5,
    "Retention": {
      "IntervalMinutes": 60,
      "RetentionDays": 7,
      "BatchSize": 500
    }
  }
}
```

The `OutboxMessage` entity and its EF configuration live in `Persistence/`.

## Correlation ID Propagation

Correlation IDs flow through the entire messaging pipeline:
1. HTTP request → `CorrelationIdMiddleware` extracts/generates `X-Correlation-Id`
2. `ICorrelationIdAccessor` makes it available to `{SolutionName}DbContext`
3. `SaveChangesAsync` stores it on the `OutboxMessage.CorrelationId` column
4. `OutboxProcessor` includes it in MassTransit message headers on publish
5. Consumers extract it from headers and push to Serilog `LogContext`

```csharp
// In a consumer:
public Task Consume(ConsumeContext<{Entity}ShippedEvent> context)
{
    var correlationId = context.Headers.Get<string>("X-Correlation-Id");
    using (LogContext.PushProperty("CorrelationId", correlationId ?? Guid.NewGuid().ToString()))
    {
        _logger.LogInformation("Processing {Entity}ShippedEvent for {EntityId}", context.Message.{Entity}Id);
        // ...
    }
    return Task.CompletedTask;
}
```
