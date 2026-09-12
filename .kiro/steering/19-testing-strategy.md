---
inclusion: manual
---

# Testing Strategy & Techniques

This document covers testing philosophy, the testing pyramid, test double taxonomy, isolation rules, and techniques for specific scenarios. For test naming, framework setup, and code patterns, see `07-testing-conventions.md`.

## Testing Pyramid

Distribute tests according to the pyramid — more fast/cheap tests at the base, fewer slow/expensive tests at the top:

```
        ╱╲
       ╱  ╲         End-to-End (E2E)
      ╱────╲        Few — full system, real infra, slow
     ╱      ╲
    ╱────────╲      Integration
   ╱          ╲    Moderate — real DB (Testcontainers), WebApplicationFactory
  ╱────────────╲
 ╱              ╲   Unit + Property-Based
╱────────────────╲  Many — fast, isolated, deterministic
```

### Distribution Guidelines

| Layer | Test Type | Speed | Count | Tooling |
|-------|-----------|-------|-------|---------|
| Domain | Unit + Property-based | < 1ms each | High | xUnit, FsCheck |
| Application | Unit (mocked deps) | < 10ms each | High | xUnit, Moq, FluentAssertions |
| Infrastructure | Integration (real DB) | < 500ms each | Moderate | Testcontainers, FsCheck |
| Api | Integration (HTTP pipeline) | < 500ms each | Moderate | WebApplicationFactory |
| Architecture | Static analysis | < 100ms each | Low | NetArchTest |
| Frontend | Unit + Component | < 50ms each | High | Vitest, testing-library |
| Frontend | Property-based | < 100ms each | Moderate | fast-check |

### What to Test at Each Level

| Level | Tests | Does NOT Test |
|-------|-------|---------------|
| Unit (Domain) | Business rules, invariants, state transitions, computed values | Persistence, serialization, HTTP |
| Unit (Application) | Handler orchestration, correct method calls on dependencies | Actual DB, real message broker |
| Integration (Infra) | EF Core mappings, outbox serialization, query correctness | Business rules (tested at unit level) |
| Integration (Api) | Full request pipeline, middleware, auth, validation → response | External services (mocked) |
| E2E | Critical user journeys across multiple services | Everything (use sparingly) |

## Arrange-Act-Assert (AAA)

Every test follows the AAA structure with clear visual separation:

```csharp
[Fact]
public void HappyPath_ComputesTotalFromLines()
{
    // Arrange
    var lines = new List<{Entity}Line>
    {
        {Entity}Line.Create(ProductId.New(), 3, new Money(5.00m, "USD")),
        {Entity}Line.Create(ProductId.New(), 2, new Money(10.00m, "USD"))
    };

    // Act
    var entity = {Entity}.Create(CustomerId.New(), lines);

    // Assert
    Assert.Equal(new Money(35.00m, "USD"), entity.Total);
}
```

Rules:
- Each test has exactly ONE act — if you need multiple acts, write multiple tests
- Arrange sets up preconditions (test data, mocks, initial state)
- Act performs the single operation under test
- Assert verifies the expected outcome (state change, return value, or interaction)
- Separate the three sections with blank lines or comments for readability

## Test Doubles Taxonomy

### When to Use Each Type

| Double | What It Does | When to Use | Example |
|--------|-------------|-------------|---------|
| **Stub** | Returns canned data, no verification | Provide indirect input to SUT | Repository returns a known entity |
| **Mock** | Records interactions, verifies calls | Verify SUT calls a dependency correctly | Verify `SaveAsync` called once |
| **Fake** | Working implementation (simplified) | Replace expensive infra with fast alternative | In-memory DbContext, InMemory MassTransit |
| **Spy** | Records calls without changing behaviour | Observe without affecting the real implementation | Wrapped logger that counts calls |

### Implementation with Moq

```csharp
// STUB: provides data, no verification
_repoMock.Setup(r => r.GetByIdAsync(It.IsAny<{Entity}Id>(), It.IsAny<CancellationToken>()))
    .ReturnsAsync(testEntity);

// MOCK: verifies interaction
_repoMock.Verify(r => r.SaveAsync(It.IsAny<{Entity}>(), It.IsAny<CancellationToken>()), Times.Once());

// FAKE: real implementation with simplified backing
// InMemory DbContext, InMemory MassTransit TestHarness
```

### Rules

- Prefer stubs over mocks — test state, not interactions (when possible)
- Mock only at layer boundaries (repository interface, event publisher interface)
- Never mock the SUT (system under test) — mock its dependencies
- Never mock domain entities or value objects — they're pure logic, just use them
- If a test needs more than 3 mocks, the SUT likely has too many dependencies (SRP violation)

## Test Isolation & Determinism

### Every Test Must Be FIRST

| Property | Meaning | How |
|----------|---------|-----|
| **F**ast | Runs in milliseconds | No I/O in unit tests, Testcontainers for integration |
| **I**solated | Independent of other tests | No shared mutable state, no execution order dependency |
| **R**epeatable | Same result every time | No randomness without seeding, no time-dependency |
| **S**elf-validating | Pass or fail automatically | Assertions built in, no manual inspection |
| **T**imely | Written close to the code | Write tests alongside implementation, not after |

### Isolation Rules

```csharp
// BAD: Tests share mutable state
private static List<{Entity}> _sharedEntities = new(); // Modified by multiple tests!

// GOOD: Each test creates its own state
[Fact]
public void Test1()
{
    var entities = new List<{Entity}> { /* fresh for this test */ };
}
```

Rules:
- No `static` mutable state shared between tests
- Each test creates its own test data (use Fakers for complex data)
- Integration tests reset database between test classes (handled by `IntegrationTestBase`)
- Never depend on test execution order — tests must pass in any sequence
- Avoid `Thread.Sleep` or `Task.Delay` — use polling with timeout for async assertions

### Deterministic Time

```csharp
// BAD: Non-deterministic
var entity = {Entity}.Create(...); // Uses DateTime.UtcNow internally
Assert.Equal(DateTime.UtcNow, entity.CreatedAt); // Flaky!

// GOOD: Inject time abstraction
public interface ITimeProvider { DateTime UtcNow { get; } }
// Or use .NET 8's built-in TimeProvider
```

Rules:
- Domain logic that depends on "now" should accept a `DateTime` parameter or use `TimeProvider`
- Never assert on `DateTime.UtcNow` directly — it creates race conditions
- For time-dependent tests, use a fake `TimeProvider` that returns a fixed value

## Testing Async Code

### CancellationToken Propagation

```csharp
[Fact]
public async Task Handle_PropagatesCancellationToken()
{
    var cts = new CancellationTokenSource();
    cts.Cancel();

    await Assert.ThrowsAsync<OperationCanceledException>(
        () => _handler.Handle(command, cts.Token));
}
```

### Timeout for Async Assertions

```csharp
// BAD: Fixed delay (slow and flaky)
await Task.Delay(1000);
Assert.True(result.IsCompleted);

// GOOD: Poll with timeout
var timeout = TimeSpan.FromSeconds(5);
var sw = Stopwatch.StartNew();
while (!result.IsCompleted && sw.Elapsed < timeout)
    await Task.Delay(50);
Assert.True(result.IsCompleted, "Timed out waiting for completion");
```

## Property-Based Testing Strategy

### When to Use Property-Based Tests

| Scenario | Why PBT Helps |
|----------|---------------|
| Domain invariants | "For ALL valid inputs, Total ≥ 0" — impossible to cover all cases with examples |
| Serialization round-trips | "For ANY entity, serialize → deserialize == original" |
| Idempotency | "For ANY event delivered N times, side effect occurs exactly once" |
| Middleware behaviour | "For ANY request, correlation header is always present on response" |
| Validation boundaries | "For ANY quantity ≤ 0, Create throws" |

### When NOT to Use PBT

- Simple happy-path scenarios (one example test is sufficient)
- UI rendering tests (hard to generate meaningful arbitrary components)
- Integration tests with real databases (too slow for 100+ iterations)

### Writing Good Properties

A property is a universally quantified statement:

```
∀ (valid inputs), invariant holds
∀ (invalid inputs), error is returned
∀ (any state + valid transition), new state satisfies postcondition
```

```csharp
[Property(MaxTest = 100)]
public Property Total_IsAlways_SumOfLineTotals()
{
    return Prop.ForAll(
        ValidEntityArbitrary(), // Custom generator for valid entities
        entity =>
        {
            var expectedTotal = entity.Lines.Sum(l => l.Quantity * l.UnitPrice.Amount);
            return (entity.Total.Amount == expectedTotal)
                .Label($"Expected {expectedTotal}, got {entity.Total.Amount}");
        });
}
```

## Frontend Testing Strategy

### Component Testing Layers

| Layer | What to Test | Tool |
|-------|-------------|------|
| Hook logic | State transformations, API error parsing | `renderHook` from testing-library |
| Component rendering | Correct elements appear for given props/state | `render` + accessible queries |
| User interaction | Click, type, submit triggers correct behaviour | `userEvent` from testing-library |
| API integration | TanStack Query hooks return correct data/states | MSW (Mock Service Worker) for API mocking |

### Accessible Queries (Priority Order)

```typescript
// BEST: Queries that reflect accessibility
screen.getByRole('button', { name: /submit/i });
screen.getByLabelText('Customer ID');
screen.getByRole('alert');

// OK: Text content
screen.getByText('Loading...');

// LAST RESORT: Test IDs (only when no accessible query works)
screen.getByTestId('complex-widget');
```

Rules:
- Prefer `getByRole` and `getByLabelText` — they verify accessibility simultaneously
- Never use class names or element structure as selectors — they're implementation details
- Use `findBy*` (async) for elements that appear after state changes
- Use `queryBy*` to assert an element does NOT exist

### Frontend Property-Based Testing

```typescript
import { fc } from '@fast-check/vitest';

// Property: Error boundary renders fallback for ANY thrown error
fc.test.prop([fc.string()])('renders error message for any error', (errorMessage) => {
  const ThrowingComponent = () => { throw new Error(errorMessage); };
  render(
    <ErrorBoundary>
      <ThrowingComponent />
    </ErrorBoundary>
  );
  expect(screen.getByRole('alert')).toBeInTheDocument();
});
```

## Contract Testing (Between Services)

When services communicate via events or HTTP, verify the contract without deploying both:

### Producer-Side Contract Test

```csharp
[Fact]
public void {Entity}PlacedEvent_SerializesToExpectedSchema()
{
    var evt = new {Entity}PlacedEvent({Entity}Id.New(), CustomerId.New(), DateTime.UtcNow);
    var json = JsonSerializer.Serialize(evt);
    var doc = JsonDocument.Parse(json);

    Assert.True(doc.RootElement.TryGetProperty("{Entity}Id", out _));
    Assert.True(doc.RootElement.TryGetProperty("CustomerId", out _));
    Assert.True(doc.RootElement.TryGetProperty("PlacedAt", out _));
}
```

### Consumer-Side Contract Test

```csharp
[Fact]
public void Consumer_DeserializesMinimalPayload()
{
    // Only required fields — proves consumer tolerates missing optional fields
    var json = """{"EntityId": "...", "CustomerId": "...", "PlacedAt": "..."}""";
    var evt = JsonSerializer.Deserialize<{Entity}PlacedEvent>(json);

    Assert.NotNull(evt);
}
```

Rules:
- Producer tests verify the event schema matches the documented contract
- Consumer tests verify the consumer can deserialize the producer's schema
- Run contract tests in CI — they catch breaking changes before deployment
- When adding optional fields, add consumer test proving it handles field absence

## What NOT to Test

| Skip Testing | Why |
|-------------|-----|
| Framework code (ASP.NET routing, EF migrations) | Already tested by Microsoft |
| Trivial property getters/setters | No logic to verify |
| Private methods directly | Test through the public API that calls them |
| Third-party library internals | Test your integration, not their code |
| Auto-generated code (Swagger clients, EF migrations) | Generated = not your responsibility |
| Configuration files (appsettings.json) | Validate at startup, not in unit tests |

## Coverage Guidance

**Target: 80% line coverage (enforced in CI)**

But coverage alone doesn't equal quality:

| High Coverage, Low Quality | Low Coverage, High Quality |
|---------------------------|---------------------------|
| Tests that assert nothing (`Assert.True(true)`) | Tests that verify business invariants |
| Tests that mirror implementation (change detector tests) | Tests that verify observable behaviour |
| 100% coverage of getters/setters | 80% coverage with property-based tests on domain logic |

### Focus Coverage On

1. Domain logic (invariants, transitions, computed values) — aim for 95%+
2. Application handlers (orchestration correctness) — aim for 90%+
3. Infrastructure (query correctness, outbox reliability) — aim for 80%+
4. Api middleware (security headers, correlation) — aim for 80%+ via PBT

### Don't Obsess Over

- Generated code coverage
- Trivial wiring code (DI registration, `Program.cs` configuration)
- Exception messages (test that exceptions are thrown, not their text)
