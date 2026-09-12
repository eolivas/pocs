---
inclusion: manual
---

# Architectural Principles: SoC, DRY, KISS, YAGNI

These four principles complement SOLID (see `12-solid-principles.md`) and guide day-to-day coding decisions across all layers.

## Separation of Concerns (SoC)

Each module, layer, class, and method should address one distinct concern.

### Layer-Level Separation

| Concern | Belongs In | NEVER In |
|---------|-----------|----------|
| Business rules & invariants | Domain | Application, Infrastructure, Api |
| Use case orchestration | Application (handlers) | Domain, Infrastructure, Api |
| Data access & external I/O | Infrastructure | Domain, Application |
| HTTP request/response | Api (endpoints, middleware) | Domain, Application |
| Validation | Application (validators) or Domain (constructor) | Api endpoints, Infrastructure |
| Logging cross-cut | Application (pipeline behaviours) | Domain entities |
| Serialization | Infrastructure or Api | Domain |

### Method-Level Separation

```csharp
// BAD: One method doing validation, persistence, notification, and logging
public async Task Handle(Place{Entity}Command request, CancellationToken ct)
{
    if (request.Lines.Count == 0) throw new Exception("No lines"); // Validation concern
    _logger.LogInformation("Placing entity");                       // Logging concern
    var entity = new {Entity}();                                    // Domain concern
    _dbContext.Add(entity);                                         // Persistence concern
    await _dbContext.SaveChangesAsync(ct);
    await _emailService.SendAsync("placed!");                       // Notification concern
}

// GOOD: Each concern handled by its dedicated component
public async Task Handle(Place{Entity}Command request, CancellationToken ct)
{
    // Validation: handled by ValidationBehaviour in pipeline (before this runs)
    // Logging: handled by LoggingBehaviour in pipeline
    var entity = {Entity}.Create(request.CustomerId, MapLines(request.Lines)); // Domain
    await _repo.SaveAsync(entity, ct);                                          // Infrastructure
    await _publisher.PublishAsync(entity.DomainEvents, ct);                     // Async notification
}
```

### Rules

- If you're writing `if` statements that check HTTP-specific things in a handler — move it to the endpoint
- If you're writing database queries in a domain entity — move it to a repository
- If you're writing email templates in a command handler — move it to a notification consumer
- Middleware handles HTTP concerns (headers, status codes, correlation) — handlers handle business logic
- Each pipeline behaviour handles exactly one cross-cutting concern

### Testing Benefit

Proper SoC means each concern is testable in isolation:
- Domain logic: pure unit tests, no mocks
- Handlers: mock the repository and publisher
- Infrastructure: test against Testcontainers
- Middleware: test against `DefaultHttpContext`

---

## Don't Repeat Yourself (DRY)

Every piece of knowledge should have a single, unambiguous, authoritative representation in the codebase.

### Where DRY Applies

| Duplication Type | Fix |
|-----------------|-----|
| Same validation logic in endpoint AND handler | Move to `FluentValidation` validator (runs in pipeline) |
| Same mapping logic in multiple handlers | Extract to a static `From()` method on the DTO |
| Same query with slight variations | Extract to a specification or repository method |
| Same error handling in multiple endpoints | Move to `ExceptionHandlingMiddleware` |
| Same configuration reading in multiple classes | Extract to a typed options class (`IOptions<T>`) |
| Same domain rule checked in multiple aggregates | Extract to a domain service or shared value object |

### Code Examples

```csharp
// BAD: Mapping duplicated across handlers
public class GetHandler { ... OrderDto.From(order) ... }
public class ListHandler { ... new OrderDto(order.Id, order.Status, ...) ... } // Duplicate mapping!

// GOOD: Single mapping method
public record {Entity}Dto(...)
{
    public static {Entity}Dto? From({Entity}? entity) { /* single source of truth */ }
}
```

```csharp
// BAD: Same validation in two places
// In endpoint:
if (request.Lines.Count == 0) return Results.BadRequest();
// In handler:
if (command.Lines.Count == 0) throw new ValidationException(...);

// GOOD: Single validator, runs once in pipeline
public class Place{Entity}CommandValidator : AbstractValidator<Place{Entity}Command>
{
    public Place{Entity}CommandValidator()
    {
        RuleFor(x => x.Lines).NotEmpty();
    }
}
```

### Where DRY Does NOT Apply

Do NOT force DRY across service boundaries:

- Two services both need a `Money` value object → each defines its own (no shared NuGet package)
- Two services both need a customer ID → each defines its own `CustomerId` strongly-typed ID
- Event schemas are owned by publishers — consumers define their own deserialization types

Rules:
- DRY within a service: extract shared logic to a single authoritative place
- DRY across services: prefer duplication over coupling (shared libraries create hidden dependencies)
- DRY in tests: helper methods and Fakers are fine, but each test should remain readable standalone
- If extracting a shared abstraction requires parameters for every variation, you've gone too far — it's not truly the same concern

---

## Keep It Simple, Stupid (KISS)

Prefer the simplest solution that solves the problem correctly.

### Application in This Project

| Complex Approach | Simpler Alternative | When Simple Is Enough |
|-----------------|--------------------|-----------------------|
| Custom event bus | MassTransit with conventions | Unless you need non-standard topology |
| Hand-rolled mediator | MediatR | Unless you need cross-process mediation |
| Custom ORM mapping | EF Core fluent API | Unless EF can't express your mapping |
| Distributed caching + Redis | `AsNoTracking()` + index | When traffic is low (< 1000 QPS) |
| CQRS with separate read DB | Same DB, different query path | Until read QPS crosses 10,000 |
| Saga state machine | Simple event → consumer | When the workflow has ≤ 2 steps |
| Generic `Repository<T>` base class | Specific `I{Entity}Repository` per aggregate | Always — generics hide domain intent |

### Code Examples

```csharp
// BAD: Over-engineered for a simple case
public interface ISpecification<T> { Expression<Func<T, bool>> Criteria { get; } }
public class Active{Entities}Specification : ISpecification<{Entity}> { ... }
public class {Entity}Repository
{
    public Task<List<{Entity}>> FindAsync(ISpecification<{Entity}> spec) { ... }
}
// Used once, in one query. The specification adds indirection with no benefit.

// GOOD: Just write the query
public async Task<List<{Entity}>> GetActiveAsync(CancellationToken ct)
{
    return await _dbContext.Set<{Entity}>()
        .AsNoTracking()
        .Where(e => e.Status != {Entity}Status.Cancelled)
        .ToListAsync(ct);
}
// Introduce Specification pattern only when you have 3+ composable query criteria
```

### Rules

- Start with the simplest implementation that passes tests
- Add complexity only when a concrete requirement or performance metric demands it
- If a pattern requires explaining to every new team member, question whether it's earning its keep
- Readable code > clever code — the next reader might be you in 6 months
- One level of indirection is fine; three levels suggests over-engineering
- Premature abstraction is as harmful as premature optimization

### KISS Checklist

Before adding infrastructure:
1. Can I solve this with a method? → Don't create a class
2. Can I solve this with a class? → Don't create an interface
3. Can I solve this with an interface? → Don't create a framework
4. Can I solve this without a new NuGet package? → Don't add one

---

## You Aren't Gonna Need It (YAGNI)

Don't implement functionality until you have a concrete, immediate requirement.

### Common YAGNI Violations

| "Just in case" Addition | Why It's Wasteful |
|------------------------|-------------------|
| Generic `Repository<T>` base class | Every aggregate has different query needs |
| Abstract factory for creating entities | `static Create()` is sufficient until you have 3+ creation strategies |
| Plugin architecture for consumers | You have 2 consumers — use direct implementation |
| Multi-tenancy support from day one | You have one tenant until proven otherwise |
| GraphQL alongside REST | Build what users need now, not what they might need later |
| Custom caching framework | Use `IDistributedCache` when performance requires it |
| Feature flag infrastructure | Use `IConfiguration` booleans until you need rollout percentages |

### Code Examples

```csharp
// BAD: Building for imaginary future requirements
public interface I{Entity}RepositoryFactory
{
    I{Entity}Repository Create(string tenantId, string region);
}
// You have one tenant in one region. This is pure speculation.

// GOOD: Build what you need now
public interface I{Entity}Repository
{
    Task<{Entity}?> GetByIdAsync({Entity}Id id, CancellationToken ct);
    Task SaveAsync({Entity} entity, CancellationToken ct);
}
// When multi-tenancy is required, you'll know the exact requirements.
```

```csharp
// BAD: Premature caching with no evidence of need
public class Cached{Entity}Repository : I{Entity}Repository
{
    // 200 lines of cache-aside logic, invalidation, TTL management
}
// Peak QPS is 12. PostgreSQL handles this trivially.

// GOOD: Add caching when decision gate is triggered
// See capacity-estimation.md: Add cache when Read QPS ≥ 1,000
```

### Rules

- Implement the feature you need today, not the one you might need next quarter
- Delete dead code — commented-out code and unused methods are noise
- If a class has methods that are never called, remove them
- If an interface has methods that only one implementation uses, it's too broad
- Configuration options without a second value are premature generalization
- "But it might be useful later" is not a requirement — it's speculation

### The YAGNI Test

Before writing new code, ask:
1. Is there a user story, bug report, or performance metric requiring this? → Build it
2. Am I building it because "someone might need it"? → Don't build it
3. Am I adding a parameter "for flexibility"? → Remove it until flexibility is needed
4. Am I creating an abstraction with only one implementation? → Inline it

---

## Principles Interaction Guide

These principles sometimes tension against each other. Here's how to resolve conflicts:

| Conflict | Resolution |
|----------|-----------|
| DRY vs. SoC | If extracting shared code forces two concerns into one class, prefer duplication |
| DRY vs. YAGNI | If the shared abstraction requires speculative parameters, prefer duplication |
| KISS vs. DRY | If the DRY extraction makes the code harder to follow, prefer local clarity |
| KISS vs. SoC | SoC wins — separation enables testing and evolution, even if it adds files |
| YAGNI vs. SoC | SoC wins — proper layer placement is not "future-proofing", it's structural correctness |

**Priority order when principles conflict:**
1. **Correctness** — code must work correctly before anything else
2. **SoC** — structural integrity enables everything else
3. **KISS** — simplicity within correct structure
4. **YAGNI** — don't build what isn't needed
5. **DRY** — eliminate duplication only when it doesn't violate the above
