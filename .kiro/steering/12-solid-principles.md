---
inclusion: manual
---

# SOLID Principles Best Practices

This project enforces SOLID principles across all layers. Follow these guidelines when adding or modifying code.

## Single Responsibility Principle (SRP)

Every class, module, and function should have exactly one reason to change.

### Application in This Project

| Layer | SRP Enforcement |
|-------|-----------------|
| Domain | One aggregate root per business concept. Each entity encapsulates its own invariants. Value objects handle one piece of domain knowledge. |
| Application | One handler per command or query. One validator per command. One behaviour per cross-cutting concern. |
| Infrastructure | One repository per aggregate root. One consumer per event. One configuration class per entity. |
| Api | One endpoint group per resource. One middleware per cross-cutting concern. |

### Rules

- A command handler MUST NOT also handle queries — separate them into distinct classes
- A domain entity MUST NOT contain persistence logic, serialization, or HTTP concerns
- If a class has more than one public method doing unrelated work, split it
- Validators live alongside commands but in separate files (`Place{Entity}CommandValidator.cs`)
- Each `IEntityTypeConfiguration<T>` handles exactly one entity's mapping

### Anti-Patterns to Avoid

```csharp
// BAD: Handler doing validation, persistence, and notification
public class Do{Everything}Handler
{
    public async Task Handle(...)
    {
        Validate(request);          // Should be in ValidationBehaviour
        await _dbContext.SaveAsync(); // Should be in repository
        await SendEmail();           // Should be in a separate consumer
    }
}

// GOOD: Handler orchestrates, delegates to specialized components
public class Place{Entity}Handler
{
    public async Task Handle(...)
    {
        var entity = {Entity}.Create(request.CustomerId, lines); // Domain handles invariants
        await _repo.SaveAsync(entity, ct);                       // Repo handles persistence
        await _publisher.PublishAsync(domainEvent, ct);           // Publisher handles delivery
    }
}
```

## Open/Closed Principle (OCP)

Classes should be open for extension but closed for modification.

### Application in This Project

- **Pipeline Behaviours**: Add new cross-cutting concerns (caching, authorization) by creating a new `IPipelineBehavior<,>` — no existing handler code changes
- **MassTransit Consumers**: Add new event reactions by creating a new `IConsumer<T>` class — no publisher code changes
- **Middleware Pipeline**: Insert new middleware without modifying existing middleware classes
- **Entity Configurations**: Add new `IEntityTypeConfiguration<T>` classes — DbContext picks them up via assembly scanning
- **Endpoint Groups**: Register new `Map{Resource}Endpoints()` without touching existing endpoint classes

### Rules

- Extend via new classes, not by modifying existing ones
- Use interfaces and abstractions at layer boundaries to allow new implementations
- Prefer composition (decorator, pipeline) over inheritance for adding behaviour
- Configuration-driven toggles (`IConfiguration`) for feature variation — not `if` chains in business logic

### Anti-Patterns to Avoid

```csharp
// BAD: Modifying existing handler to add new behaviour
public class Place{Entity}Handler
{
    public async Task Handle(...)
    {
        // Added logging here (violates OCP — should be a behaviour)
        _logger.LogInformation("Placing entity...");
        // Added caching here (violates OCP — should be a behaviour)
        await _cache.InvalidateAsync("entities");
    }
}

// GOOD: Logging and caching as separate pipeline behaviours
// LoggingBehaviour<,> and CachingBehaviour<,> — handler untouched
```

## Liskov Substitution Principle (LSP)

Subtypes must be substitutable for their base types without altering program correctness.

### Application in This Project

- **Repository Interfaces**: `I{Entity}Repository` (Domain) is implemented by `Ef{Entity}Repository` (Infrastructure). Swapping to a Dapper-based implementation MUST preserve the same contract — same exceptions, same semantics
- **Entity Hierarchy**: `Entity<TId>` and `AggregateRoot<TId>` base classes define contracts that derived types must honour (e.g., `DomainEvents` collection, `Id` property)
- **Value Objects**: All `record` value objects must be immutable and validate on construction — any subtype must enforce at least the same invariants

### Rules

- Never weaken preconditions in derived classes (don't accept broader input than the base)
- Never strengthen postconditions (don't return narrower results than the base promises)
- If an interface declares a method throws on invalid input, all implementations must throw on the same invalid input
- Test substitutability: if you swap `InMemoryRepository` for `EfRepository` in tests, behaviour should remain identical

### Anti-Patterns to Avoid

```csharp
// BAD: Implementation breaks the contract
public class CachedRepository : I{Entity}Repository
{
    public async Task<{Entity}?> GetByIdAsync({Entity}Id id, CancellationToken ct)
    {
        // Returns stale data from cache — violates "get latest" semantics
        return _cache.Get(id);
    }
}

// GOOD: Cache-aside that preserves semantics
public class CachedRepository : I{Entity}Repository
{
    public async Task<{Entity}?> GetByIdAsync({Entity}Id id, CancellationToken ct)
    {
        var cached = await _cache.GetAsync(id);
        if (cached is not null) return cached;
        var entity = await _inner.GetByIdAsync(id, ct); // Falls through to real repo
        await _cache.SetAsync(id, entity);
        return entity;
    }
}
```

## Interface Segregation Principle (ISP)

Clients should not depend on interfaces they do not use.

### Application in This Project

- **Repository Interfaces**: Define only the methods the Application layer needs (`GetByIdAsync`, `SaveAsync`) — not every possible CRUD operation
- **Application Interfaces**: `IApplicationEventPublisher` exposes only `PublishAsync` — not broker connection management
- **MediatR**: `ISender` (send commands/queries) is preferred over `IMediator` (which also includes notifications) in endpoint injection
- **Domain Events**: Each event carries only the data needed by its consumers — not the full aggregate state

### Rules

- Prefer small, focused interfaces over large "god" interfaces
- Inject `ISender` in endpoints, not `IMediator` (narrower interface)
- Repository interfaces live in Domain and expose only domain-meaningful operations
- If an interface has methods that some implementations leave as `throw new NotImplementedException()`, the interface is too broad — split it

### Anti-Patterns to Avoid

```csharp
// BAD: Fat interface forcing unused implementations
public interface I{Entity}Repository
{
    Task<{Entity}?> GetByIdAsync({Entity}Id id, CancellationToken ct);
    Task SaveAsync({Entity} entity, CancellationToken ct);
    Task DeleteAsync({Entity}Id id, CancellationToken ct);        // Not all consumers need this
    Task<IList<{Entity}>> SearchAsync(string query, CancellationToken ct); // Not all consumers need this
    Task BulkInsertAsync(IEnumerable<{Entity}> entities, CancellationToken ct); // Definitely not
}

// GOOD: Focused interface for what handlers actually need
public interface I{Entity}Repository
{
    Task<{Entity}?> GetByIdAsync({Entity}Id id, CancellationToken ct);
    Task SaveAsync({Entity} entity, CancellationToken ct);
}
```

## Dependency Inversion Principle (DIP)

High-level modules should not depend on low-level modules. Both should depend on abstractions.

### Application in This Project

- **Layer Boundaries**: Domain defines `I{Entity}Repository` — Infrastructure implements it. Domain never references EF Core.
- **Application Interfaces**: `IApplicationEventPublisher` is defined in Application — implemented by `MassTransitEventPublisher` in Infrastructure
- **DI Registration**: All wiring happens in `Program.cs` (composition root). Inner layers are unaware of outer-layer implementations.
- **Architecture Tests**: NetArchTest enforces that Domain does not reference Infrastructure, and Application does not reference Api

### Rules

- Domain layer MUST have zero NuGet dependencies beyond the base SDK
- Application layer defines interfaces in `Interfaces/` — Infrastructure implements them
- Never inject concrete classes across layer boundaries — always use interfaces
- The composition root (`Program.cs`) is the only place that knows about all concrete types
- Architecture tests (`{SolutionName}.Architecture.Tests`) enforce dependency direction at build time

### Anti-Patterns to Avoid

```csharp
// BAD: Handler depends on infrastructure concrete type
using {SolutionName}.Infrastructure.Persistence;

public class Place{Entity}Handler
{
    private readonly {SolutionName}DbContext _dbContext; // Direct infrastructure dependency!
}

// GOOD: Handler depends on abstraction defined in Domain
using {SolutionName}.Domain;

public class Place{Entity}Handler
{
    private readonly I{Entity}Repository _repo; // Abstraction — implementation injected via DI
}
```

## SOLID Checklist for New Code

Before submitting a PR, verify:

1. **SRP** — Does each new class have exactly one reason to change?
2. **OCP** — Did you extend behaviour without modifying existing classes?
3. **LSP** — Do new implementations honour the full contract of their interfaces?
4. **ISP** — Are interfaces focused on what consumers actually need?
5. **DIP** — Do dependencies point inward (toward abstractions), never outward?
