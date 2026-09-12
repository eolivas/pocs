---
inclusion: manual
---

# Design Patterns Best Practices

This project uses well-established design patterns across all layers. Follow these guidelines when applying or recognizing patterns in the codebase.

## Creational Patterns

### Factory Method

Used for creating domain objects with validated invariants.

**Where**: Domain layer — aggregate roots, entities, value objects

```csharp
// Static factory method on aggregate root
public static {Entity} Create(CustomerId customerId, IReadOnlyList<{Entity}Line> lines)
{
    if (lines is null || lines.Count == 0)
        throw new {Entity}DomainException("Must have at least one line.");

    var entity = new {Entity}
    {
        Id = {Entity}Id.New(),
        CustomerId = customerId,
        Status = {Entity}Status.Pending
    };

    entity._lines.AddRange(lines);
    entity.RaiseDomainEvent(new {Entity}CreatedEvent(entity.Id, customerId));
    return entity;
}
```

Rules:
- Constructors are `private` — instantiation goes through `Create(...)` or `New()`
- Factory methods validate all invariants before object creation
- Factory methods raise domain events for significant state transitions
- Strongly-typed IDs use `static New()` as a factory: `{Entity}Id.New()`

### Builder (Implicit)

Used for complex test data generation via Bogus Fakers.

**Where**: Test projects — `{Entity}Faker.cs`

```csharp
public class {Entity}Faker
{
    public static {Entity} CreateValid(int lineCount = 1)
    {
        var lines = Enumerable.Range(0, lineCount)
            .Select(_ => {Entity}Line.Create(ProductId.New(), 1, new Money(10m, "USD")))
            .ToList();

        return {Entity}.Create(CustomerId.New(), lines);
    }
}
```

Rules:
- One Faker per aggregate root
- Sensible defaults for all parameters
- Optional parameters for specific test scenarios

## Structural Patterns

### Repository

Abstracts data access behind a domain-meaningful interface.

**Where**: Interface in Domain, implementation in Infrastructure

```csharp
// Domain layer — defines the contract
public interface I{Entity}Repository
{
    Task<{Entity}?> GetByIdAsync({Entity}Id id, CancellationToken ct);
    Task SaveAsync({Entity} entity, CancellationToken ct);
}

// Infrastructure layer — implements with EF Core
public class Ef{Entity}Repository : I{Entity}Repository
{
    private readonly {SolutionName}DbContext _dbContext;

    public async Task<{Entity}?> GetByIdAsync({Entity}Id id, CancellationToken ct)
    {
        return await _dbContext.Set<{Entity}>()
            .Include(e => e.Lines)
            .FirstOrDefaultAsync(e => e.Id == id, ct);
    }

    public async Task SaveAsync({Entity} entity, CancellationToken ct)
    {
        _dbContext.Set<{Entity}>().Add(entity);
        await _dbContext.SaveChangesAsync(ct);
    }
}
```

Rules:
- Interface lives in Domain — exposes only domain-meaningful operations
- Implementation lives in Infrastructure — uses EF Core, Dapper, or HTTP client
- Always load aggregates complete (with child entities via `.Include()`)
- One repository per aggregate root

### Decorator

Wraps existing behaviour with additional concerns without modifying the original.

**Where**: Infrastructure layer — caching, logging wrappers

```csharp
// Decorator that adds caching around an existing repository
public class Cached{Entity}Repository : I{Entity}Repository
{
    private readonly I{Entity}Repository _inner;
    private readonly IDistributedCache _cache;

    public Cached{Entity}Repository(I{Entity}Repository inner, IDistributedCache cache)
    {
        _inner = inner;
        _cache = cache;
    }

    public async Task<{Entity}?> GetByIdAsync({Entity}Id id, CancellationToken ct)
    {
        var cached = await _cache.GetAsync<{Entity}>(id.Value.ToString());
        if (cached is not null) return cached;

        var entity = await _inner.GetByIdAsync(id, ct);
        if (entity is not null)
            await _cache.SetAsync(id.Value.ToString(), entity, TimeSpan.FromMinutes(5));

        return entity;
    }
}
```

Rules:
- Decorator implements the same interface as the wrapped class
- Register via DI decoration — do not modify the inner class
- Keep decorators focused on one cross-cutting concern (caching, logging, metrics)
- Chain decorators for multiple concerns: `Logging → Caching → Real`

### Adapter

Translates between incompatible interfaces (external systems ↔ domain contracts).

**Where**: Infrastructure layer — HTTP clients, message broker adapters

```csharp
// Application-level abstraction
public interface IApplicationEventPublisher
{
    Task PublishAsync(DomainEvent domainEvent, CancellationToken ct);
}

// Infrastructure adapter — translates to MassTransit
public class MassTransitEventPublisher : IApplicationEventPublisher
{
    private readonly IPublishEndpoint _publishEndpoint;

    public async Task PublishAsync(DomainEvent domainEvent, CancellationToken ct)
    {
        await _publishEndpoint.Publish(domainEvent, domainEvent.GetType(), ct);
    }
}
```

Rules:
- Define the interface in the layer that needs it (Application or Domain)
- Implement the adapter in Infrastructure
- The adapter handles all translation (serialization, protocol mapping, error wrapping)

## Behavioural Patterns

### Mediator

Decouples request senders from handlers through a central dispatcher.

**Where**: Application layer via MediatR

```csharp
// Endpoint sends command — doesn't know which handler processes it
group.MapPost("/", async (Place{Entity}Request request, ISender sender) =>
{
    var id = await sender.Send(new Place{Entity}Command { ... });
    return Results.Created($"/api/{entities}/{id}", new { id });
});
```

Rules:
- Endpoints inject `ISender` (not `IMediator`) — narrower interface
- One handler per request type
- Cross-cutting concerns live in pipeline behaviours, not handlers
- Commands return IDs or Unit; queries return DTOs

### Chain of Responsibility (Pipeline Behaviours)

Processes a request through a chain of handlers, each deciding whether to pass it along.

**Where**: Application layer — `IPipelineBehavior<,>` implementations

```csharp
// Each behaviour wraps the next in the pipeline
public class ValidationBehaviour<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
{
    private readonly IEnumerable<IValidator<TRequest>> _validators;

    public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken ct)
    {
        // Pre-processing: validate
        var failures = _validators.SelectMany(v => v.Validate(request).Errors).ToList();
        if (failures.Count > 0)
            throw new ValidationException(failures);

        // Pass to next in chain
        return await next();
    }
}
```

Pipeline order:
1. `LoggingBehaviour` — logs request name and elapsed time
2. `ValidationBehaviour` — runs FluentValidation, short-circuits on failure
3. Handler — executes business logic

Rules:
- Each behaviour has a single responsibility
- Behaviours can short-circuit the pipeline (e.g., validation failure)
- Register order matters — outermost behaviour runs first
- New behaviours are added without modifying existing ones (OCP)

### Observer (Domain Events)

Objects subscribe to state changes in other objects without tight coupling.

**Where**: Domain layer (event raising) → Infrastructure layer (event handling)

```csharp
// Aggregate raises events (publisher)
public void Place()
{
    if (Status != {Entity}Status.Pending)
        throw new {Entity}DomainException("Can only place pending entities.");

    Status = {Entity}Status.Placed;
    RaiseDomainEvent(new {Entity}PlacedEvent(Id));
}

// Consumer reacts to events (subscriber)
public sealed class {Entity}PlacedConsumer : IConsumer<{Entity}PlacedEvent>
{
    public Task Consume(ConsumeContext<{Entity}PlacedEvent> context)
    {
        // Send notification, update read model, etc.
        return Task.CompletedTask;
    }
}
```

Rules:
- Aggregates raise events via `RaiseDomainEvent(...)` — they don't know who listens
- Events are serialized to the outbox table in the same transaction as the state change
- Consumers are idempotent — the same event may be delivered more than once
- One consumer per event per side effect

### Strategy (Configuration-Driven)

Selects behaviour at runtime based on configuration or context.

**Where**: Infrastructure layer — transport selection, provider switching

```csharp
// MassTransit transport strategy — selected via configuration
if (configuration["RabbitMq:Host"] is not null)
{
    cfg.UsingRabbitMq((context, rabbitCfg) => { /* RabbitMQ strategy */ });
}
else
{
    cfg.UsingInMemory((context, inMemoryCfg) => { /* InMemory strategy */ });
}
```

Rules:
- Strategy selection happens at startup (composition root) or via `IConfiguration`
- Each strategy implements the same interface — consumers are unaware of which is active
- Prefer configuration-driven selection over runtime `if` chains in business logic
- Document the strategy options in `appsettings.json` with sensible defaults

### Template Method (Middleware)

Defines a skeleton algorithm with extension points.

**Where**: Api layer — ASP.NET Core middleware

```csharp
public class MyCustomMiddleware
{
    private readonly RequestDelegate _next;

    public MyCustomMiddleware(RequestDelegate next) => _next = next;

    public async Task InvokeAsync(HttpContext context)
    {
        // Pre-processing (template step 1)
        BeforeRequest(context);

        await _next(context); // Delegate to next middleware

        // Post-processing (template step 2)
        AfterRequest(context);
    }
}
```

Rules:
- Middleware follows the `pre → next → post` structure
- Each middleware handles one concern (security headers, correlation ID, rate limiting)
- Register in the correct pipeline position (see `11-middleware-security-observability.md`)
- Write property-based tests proving universal behaviour

## Architectural Patterns

### CQRS (Command Query Responsibility Segregation)

Separates read and write models for different optimization strategies.

**Where**: Application layer — `Commands/` vs `Queries/`

Rules:
- Commands change state, return IDs or Unit
- Queries read state, return DTOs — never modify data
- Handlers never do both — a handler is either a command handler or a query handler
- Different optimization paths: queries can use `AsNoTracking()`, projections, caching

### Outbox Pattern

Guarantees at-least-once event delivery without distributed transactions.

**Where**: Infrastructure layer — `OutboxMessage` entity, `OutboxProcessor` background service

Rules:
- Events are written to `outbox_messages` in the same DB transaction as the state change
- Background service polls and publishes — marks as processed on success
- Failed messages are retried with exponential backoff, dead-lettered after max retries
- Consumers must be idempotent

### Specification Pattern

Encapsulates query criteria as composable objects.

**Where**: Infrastructure layer — `Specifications/`

```csharp
public class Active{Entities}Specification : ISpecification<{Entity}>
{
    public Expression<Func<{Entity}, bool>> Criteria =>
        e => e.Status != {Entity}Status.Cancelled;
}
```

Rules:
- One specification per meaningful business query
- Composable via AND/OR operators
- Used by repositories to filter without exposing query details to the Application layer

## Pattern Selection Guide

When adding new code, ask:

| Need | Pattern | Layer |
|------|---------|-------|
| Create domain object with validation | Factory Method | Domain |
| Abstract data access | Repository | Domain (interface), Infrastructure (impl) |
| Add cross-cutting concern to handlers | Pipeline Behaviour (Chain of Responsibility) | Application |
| Decouple endpoint from handler | Mediator | Application |
| React to state changes asynchronously | Observer (Domain Events) | Domain → Infrastructure |
| Wrap existing behaviour with new concern | Decorator | Infrastructure |
| Translate external API to internal contract | Adapter | Infrastructure |
| Select implementation at runtime | Strategy | Infrastructure / Composition Root |
| Guarantee event delivery | Outbox Pattern | Infrastructure |
| Pre/post processing on requests | Template Method (Middleware) | Api |
