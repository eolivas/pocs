---
inclusion: manual
---

# Async/Await Patterns & Best Practices

This document covers async programming rules, CancellationToken propagation, parallel execution, background tasks, and common pitfalls for both .NET backend and React frontend.

## Fundamental Rules

### Async All the Way

Once you go async, stay async through the entire call chain:

```csharp
// GOOD: Async from endpoint → handler → repository → EF Core
group.MapGet("/{id:guid}", async (Guid id, ISender sender, CancellationToken ct) =>
{
    var result = await sender.Send(new Get{Entity}Query(id), ct);
    return result is not null ? Results.Ok(result) : Results.NotFound();
});

// BAD: Blocking on async code (deadlock risk)
group.MapGet("/{id:guid}", (Guid id, ISender sender) =>
{
    var result = sender.Send(new Get{Entity}Query(id)).Result; // DEADLOCK!
    return result is not null ? Results.Ok(result) : Results.NotFound();
});
```

### Never Block on Async

| Blocking Call | Problem | Fix |
|--------------|---------|-----|
| `.Result` | Deadlock in synchronization contexts | `await` the task |
| `.Wait()` | Same deadlock risk | `await` the task |
| `.GetAwaiter().GetResult()` | No deadlock but blocks thread pool | `await` the task |
| `Task.Run(() => ...).Wait()` | Wastes two threads | Just `await` directly |
| `Thread.Sleep()` | Blocks thread pool thread | `await Task.Delay()` |

**Rule:** If you find yourself using any of these, the calling method should be `async`.

### When Sync Is Acceptable

| Scenario | Sync OK? | Reason |
|----------|----------|--------|
| Domain entity logic (invariant checks, computations) | Yes | Pure CPU work, no I/O |
| Value object construction and validation | Yes | In-memory, instantaneous |
| Static factory methods (`{Entity}.Create(...)`) | Yes | No I/O involved |
| Extension methods doing in-memory transforms | Yes | Collection operations |
| Program.cs DI registration | Yes | Startup-only, runs once |

**Rule:** Only I/O-bound operations need `async`. Domain logic stays synchronous.

## CancellationToken Propagation

### Always Accept and Forward

```csharp
// Handler accepts CancellationToken
public async Task<{Entity}Id> Handle(Place{Entity}Command request, CancellationToken ct)
{
    var entity = {Entity}.Create(request.CustomerId, lines);
    await _repo.SaveAsync(entity, ct);           // Forward to repository
    await _publisher.PublishAsync(event, ct);     // Forward to publisher
    return entity.Id;
}

// Repository forwards to EF Core
public async Task SaveAsync({Entity} entity, CancellationToken ct)
{
    _dbContext.Set<{Entity}>().Add(entity);
    await _dbContext.SaveChangesAsync(ct);        // Forward to EF Core
}
```

### Rules

- Every `async` method MUST accept `CancellationToken` as its last parameter
- Always pass the token to the next async call in the chain
- Name it `ct` (short, consistent across the project)
- MediatR handlers receive it automatically via the `Handle` method signature
- Endpoint handlers receive it by declaring `CancellationToken ct` as a parameter

### Checking Cancellation in Loops

```csharp
// Background service processing in a loop
protected override async Task ExecuteAsync(CancellationToken ct)
{
    while (!ct.IsCancellationRequested)
    {
        await ProcessBatchAsync(ct);
        await Task.Delay(TimeSpan.FromSeconds(5), ct); // Throws on cancellation
    }
}

// CPU-intensive loop (cooperative cancellation)
foreach (var item in largeCollection)
{
    ct.ThrowIfCancellationRequested(); // Check periodically in CPU-bound work
    Process(item);
}
```

### Cancellation in HTTP Requests

When a client disconnects, ASP.NET Core signals the `CancellationToken`:

```csharp
group.MapGet("/expensive", async (ISender sender, CancellationToken ct) =>
{
    // If client disconnects, ct is cancelled → downstream calls abort
    var result = await sender.Send(new ExpensiveQuery(), ct);
    return Results.Ok(result);
});
```

**Rule:** Respecting cancellation prevents wasted work on abandoned requests.

## Task vs. ValueTask

### When to Use Each

| Type | Use When | Allocation |
|------|----------|-----------|
| `Task<T>` | Default choice — always safe | Allocates on heap (one object per call) |
| `ValueTask<T>` | Hot path where result is often synchronous (cache hit) | Stack-allocated when synchronous |

```csharp
// ValueTask — useful when the result is often cached/immediate
public ValueTask<{Entity}?> GetByIdAsync({Entity}Id id, CancellationToken ct)
{
    if (_cache.TryGet(id, out var cached))
        return ValueTask.FromResult(cached); // No allocation!

    return new ValueTask<{Entity}?>(LoadFromDatabaseAsync(id, ct)); // Falls back to Task
}
```

### Rules

- Use `Task<T>` by default — it's simpler and always correct
- Use `ValueTask<T>` only on measured hot paths where sync completion is common
- Never `await` a `ValueTask` more than once (undefined behaviour)
- Never use `.Result` on `ValueTask` before it completes
- Interface methods (`I{Entity}Repository`) use `Task<T>` — simpler for consumers

## Parallel Async Operations

### Task.WhenAll (Fan-Out)

Run independent async operations concurrently:

```csharp
// GOOD: Independent operations run in parallel
var customerTask = _customerService.GetAsync(customerId, ct);
var inventoryTask = _inventoryService.CheckAsync(productId, ct);
var pricingTask = _pricingService.GetPriceAsync(productId, ct);

await Task.WhenAll(customerTask, inventoryTask, pricingTask);

var customer = await customerTask;  // Already completed — no additional await
var inventory = await inventoryTask;
var pricing = await pricingTask;
```

```csharp
// BAD: Sequential when operations are independent
var customer = await _customerService.GetAsync(customerId, ct);   // Wait...
var inventory = await _inventoryService.CheckAsync(productId, ct); // Wait...
var pricing = await _pricingService.GetPriceAsync(productId, ct);  // Wait...
// Total time = sum of all three
```

### Rules

- Use `Task.WhenAll` when operations are independent (no data dependency between them)
- If operation B needs the result of A, they must be sequential
- Limit concurrency when fan-out is large (avoid overwhelming downstream services)
- Handle partial failures: if one task fails in `WhenAll`, the aggregate exception contains all failures

### Parallel.ForEachAsync (Bounded Concurrency)

Process a collection with controlled parallelism:

```csharp
await Parallel.ForEachAsync(items, new ParallelOptions
{
    MaxDegreeOfParallelism = 10,
    CancellationToken = ct
}, async (item, token) =>
{
    await ProcessItemAsync(item, token);
});
```

### Rules

- Always set `MaxDegreeOfParallelism` — unbounded parallelism overwhelms resources
- Use for batch processing in background services (not in request handlers)
- Each iteration gets its own `CancellationToken` — respect it

## Background Tasks

### BackgroundService Pattern

```csharp
public class OutboxProcessor : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IOptionsMonitor<OutboxOptions> _options;
    private readonly ILogger<OutboxProcessor> _logger;

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        _logger.LogInformation("OutboxProcessor started");

        while (!ct.IsCancellationRequested)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var dbContext = scope.ServiceProvider.GetRequiredService<{SolutionName}DbContext>();
                await ProcessBatchAsync(dbContext, ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                break; // Graceful shutdown
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "OutboxProcessor error — retrying after delay");
            }

            await Task.Delay(TimeSpan.FromSeconds(_options.CurrentValue.PollingIntervalSeconds), ct);
        }

        _logger.LogInformation("OutboxProcessor stopped");
    }
}
```

### Rules

- Create a new `IServiceScope` per iteration (DbContext is scoped, not singleton)
- Catch `OperationCanceledException` for graceful shutdown
- Catch general exceptions to prevent the service from crashing on transient errors
- Log start/stop for visibility in container logs
- Use `IOptionsMonitor<T>` for hot-reloadable configuration in long-running services

### Fire-and-Forget (Avoid)

```csharp
// BAD: Fire-and-forget loses exceptions silently
_ = SendNotificationAsync(entity.Id);

// BAD: async void — unobserved exceptions crash the process
async void SendNotification() { await _email.SendAsync(...); }

// GOOD: Use domain events + outbox for async side effects
entity.RaiseDomainEvent(new {Entity}PlacedEvent(entity.Id));
// OutboxProcessor handles delivery with retry and error tracking
```

**Rule:** Never use fire-and-forget in request handlers. Use the outbox pattern for reliable async delivery.

## Async Streams (IAsyncEnumerable)

For streaming large result sets without loading everything into memory:

```csharp
// Repository returns async stream
public async IAsyncEnumerable<OutboxMessage> GetUnprocessedAsync(
    [EnumeratorCancellation] CancellationToken ct)
{
    await foreach (var message in _dbContext.Set<OutboxMessage>()
        .Where(m => m.ProcessedAt == null)
        .AsAsyncEnumerable()
        .WithCancellation(ct))
    {
        yield return message;
    }
}

// Consumer iterates without buffering all results
await foreach (var message in _repo.GetUnprocessedAsync(ct))
{
    await ProcessAsync(message, ct);
}
```

### Rules

- Use `IAsyncEnumerable<T>` when processing large datasets one-at-a-time
- Apply `[EnumeratorCancellation]` attribute to the CancellationToken parameter
- Use `.WithCancellation(ct)` when consuming the stream
- Prefer `ToListAsync()` for small, bounded collections (simpler)
- Prefer `IAsyncEnumerable` for unbounded or very large result sets

## ConfigureAwait

### Rule for This Project: Don't Use It

```csharp
// This project does NOT use ConfigureAwait(false)
// ASP.NET Core has no synchronization context — ConfigureAwait is meaningless

// DON'T:
await _repo.GetByIdAsync(id, ct).ConfigureAwait(false); // Unnecessary noise

// DO:
await _repo.GetByIdAsync(id, ct); // Clean, readable
```

**Why:** ASP.NET Core does not have a `SynchronizationContext`. `ConfigureAwait(false)` has no effect and adds visual noise. Only library authors targeting multiple platforms (WPF, WinForms, ASP.NET Core) need it.

## Frontend Async Patterns (React)

### TanStack Query (Server State)

```typescript
// Async data fetching — TanStack Query handles loading, error, caching
export function use{Entity}(id: string) {
  return useQuery<{Entity}Dto>({
    queryKey: ['{entities}', id],
    queryFn: async () => {
      const response = await http.get<{Entity}Dto>(`/{entities}/${id}`);
      return response.data;
    },
  });
}
```

### AbortController (Request Cancellation)

```typescript
// Cancel requests when component unmounts or dependency changes
useEffect(() => {
  const controller = new AbortController();

  fetchData(controller.signal);

  return () => controller.abort(); // Cleanup on unmount
}, [dependency]);
```

**Note:** TanStack Query handles abort automatically — you don't need manual AbortController for query hooks.

### Async Event Handlers

```typescript
// GOOD: Handle errors in async handlers
const handleSubmit = async (e: FormEvent) => {
  e.preventDefault();
  try {
    await mutation.mutateAsync(formData);
  } catch (error) {
    parseError(error); // useApiError hook
  }
};

// BAD: Unhandled promise rejection
const handleSubmit = async (e: FormEvent) => {
  e.preventDefault();
  await mutation.mutateAsync(formData); // Unhandled if it throws!
};
```

### Rules

- Use TanStack Query for all server state — it handles loading, errors, caching, and cancellation
- Use `mutateAsync` (not `mutate`) when you need to handle success/error in the handler
- Always wrap `mutateAsync` in try/catch or use the `onError` callback
- AbortController is only needed for non-TanStack-Query fetches (rare)

## Common Pitfalls

| Pitfall | Problem | Fix |
|---------|---------|-----|
| `async void` | Unobserved exceptions crash the process | Return `Task`, never `async void` (except event handlers) |
| Missing `await` | Task runs but result is discarded | Always `await` or store the Task |
| `.Result` / `.Wait()` | Thread pool deadlock | `await` instead |
| `Task.Run` in ASP.NET Core | Wastes a thread pool thread for no benefit | `await` directly — ASP.NET Core is already async |
| `await` in a tight loop without yield | Starves other tasks | Use `Task.Yield()` or batch |
| Forgetting `CancellationToken` | Wasted work on cancelled requests | Always accept and forward `ct` |
| `async` method without `await` | Compiler warning, unnecessary state machine | Remove `async` keyword, return `Task.FromResult` |
| Catching `Exception` in background services without retry | Service silently stops processing | Log + continue loop |
| `ConfigureAwait(false)` everywhere | Visual noise, no effect in ASP.NET Core | Don't use it |
