---
inclusion: fileMatch
fileMatchPattern: "**/*Repository*.cs,**/*DbContext*.cs,**/Migrations/**,**/*Configuration.cs"
---

# EF Core Best Practices

This document covers performance, query optimization, migration workflow, concurrency, and common pitfalls for Entity Framework Core in this project. For entity configuration patterns (table naming, ID conversions, owned entities), see `06-efcore-entity-configuration.md`.

## Query Performance

### Use `AsNoTracking()` for Read-Only Queries

Query handlers that return DTOs should disable the change tracker:

```csharp
public async Task<{Entity}Dto?> Handle(Get{Entity}Query request, CancellationToken ct)
{
    var entity = await _dbContext.Set<{Entity}>()
        .AsNoTracking()
        .Include(e => e.Lines)
        .FirstOrDefaultAsync(e => e.Id == new {Entity}Id(request.{Entity}Id), ct);

    return {Entity}Dto.From(entity);
}
```

Rules:
- All query handlers MUST use `AsNoTracking()` — they never modify data
- Command handlers that load-then-modify MUST NOT use `AsNoTracking()` (change tracker needed)
- Repository read methods can offer both tracked and untracked variants if needed

### Use Projections to Reduce Data Transfer

When you only need a subset of fields, project directly to DTOs:

```csharp
var dto = await _dbContext.Set<{Entity}>()
    .AsNoTracking()
    .Where(e => e.Id == id)
    .Select(e => new {Entity}Dto(
        e.Id.Value,
        e.CustomerId.Value,
        e.Status.ToString(),
        e.Total.Amount,
        e.Total.Currency,
        e.Lines.Select(l => new {Entity}LineDto(/* ... */)).ToList()
    ))
    .FirstOrDefaultAsync(ct);
```

Rules:
- Prefer `Select()` projections for list queries — avoids loading full aggregates into memory
- For single-entity loads that need domain behaviour, load the full aggregate
- Never use `.ToList()` before `.Where()` — always filter in the database

### Avoid N+1 Queries

Always eager-load child entities when loading aggregates:

```csharp
// GOOD: Single query with join
var entity = await _dbContext.Set<{Entity}>()
    .Include(e => e.Lines)
    .FirstOrDefaultAsync(e => e.Id == id, ct);

// BAD: Lazy loading causes N+1 (not configured, but avoid the pattern)
var entity = await _dbContext.Set<{Entity}>().FindAsync(id);
var lines = entity.Lines; // Would trigger separate query per access
```

Rules:
- This project does NOT enable lazy loading — all navigation properties must be explicitly included
- Use `.Include()` for aggregates that always need their children
- Use `.ThenInclude()` for deeper nested entities
- For queries returning lists, consider projections instead of full includes

### Pagination

Never load unbounded collections:

```csharp
var page = await _dbContext.Set<{Entity}>()
    .AsNoTracking()
    .OrderByDescending(e => e.CreatedAt)
    .Skip((pageNumber - 1) * pageSize)
    .Take(pageSize)
    .ToListAsync(ct);
```

Rules:
- All list endpoints MUST support pagination (`Skip`/`Take` or keyset pagination)
- Default page size: 20, maximum: 100
- Always apply `OrderBy` before `Skip`/`Take` — results are non-deterministic otherwise
- For high-volume tables, prefer keyset pagination (`WHERE Id > @lastId`) over offset pagination

## Change Tracker Best Practices

### Scope DbContext to Request Lifetime

The DbContext is registered as `Scoped` (one instance per HTTP request):

```csharp
builder.Services.AddDbContext<{SolutionName}DbContext>(options =>
    options.UseNpgsql(connectionString));
```

Rules:
- Never register DbContext as `Singleton` — it's not thread-safe
- Never inject DbContext into `Singleton` services (background services get a scope via `IServiceScopeFactory`)
- Each request gets a fresh change tracker — no stale entity references across requests

### Clear the Change Tracker for Batch Operations

For background services processing batches (e.g., `OutboxProcessor`):

```csharp
foreach (var batch in messages.Chunk(batchSize))
{
    // Process batch...
    await _dbContext.SaveChangesAsync(ct);
    _dbContext.ChangeTracker.Clear(); // Prevent memory growth
}
```

Rules:
- Call `ChangeTracker.Clear()` between batches in long-running loops
- Prevents tracked entity count from growing unbounded
- Only needed in background services — request-scoped contexts are disposed automatically

### Detect Unintended Tracking

In development, enable sensitive data logging and query tags to spot tracked entities:

```csharp
options.EnableSensitiveDataLogging() // Development only!
       .EnableDetailedErrors();
```

## Concurrency Control

### Optimistic Concurrency with Row Version

For aggregates that may be modified concurrently:

```csharp
// Entity property
public uint RowVersion { get; private set; }

// Configuration
builder.Property(e => e.RowVersion)
    .IsRowVersion();
```

Rules:
- Add `RowVersion` property to aggregates that have concurrent access patterns
- EF Core automatically checks the version on `SaveChangesAsync` — throws `DbUpdateConcurrencyException` on conflict
- Handle the exception in the handler or let the exception middleware return 409 Conflict
- Value objects and child entities inherit concurrency from their owning aggregate

### Handling Concurrency Conflicts

```csharp
try
{
    await _dbContext.SaveChangesAsync(ct);
}
catch (DbUpdateConcurrencyException)
{
    // Option 1: Reload and retry (last-writer-wins)
    // Option 2: Return 409 Conflict to the client
    throw; // Let ExceptionHandlingMiddleware handle it
}
```

## Migration Best Practices

### Creating Migrations

```bash
dotnet ef migrations add {MigrationName} \
    --project src/{SolutionName}.Infrastructure \
    --startup-project src/{SolutionName}.Api
```

Naming conventions:
- PascalCase descriptive name: `AddCustomerEmailIndex`, `CreateOutboxTable`, `RenameStatusColumn`
- Never: `Update1`, `Fix`, `Changes`

### Migration Rules

- One migration per logical change — don't bundle unrelated schema changes
- Always review the generated migration code before committing
- Test migrations against a real PostgreSQL instance (CI does this automatically)
- Never modify a migration that has already been applied to shared environments
- For data migrations, use separate migration steps (schema change → data backfill → cleanup)

### Verifying Migrations in CI

The CI pipeline runs:
```bash
dotnet ef migrations has-pending-model-changes  # Fails if model diverged from latest migration
dotnet ef database update                        # Applies to temp PostgreSQL container
```

Rules:
- If you change an entity or configuration, you MUST add a migration
- The CI check catches missing migrations before merge
- Integration tests apply all migrations to Testcontainers PostgreSQL

### Rolling Back

```bash
dotnet ef database update {PreviousMigrationName}  # Reverts to a specific migration
dotnet ef migrations remove                         # Removes the last unapplied migration
```

Rules:
- Only remove migrations that have NOT been applied to any shared environment
- For applied migrations, create a new migration that reverses the change

## Connection and Pooling

### Connection Resilience

Configure retry on transient failures:

```csharp
options.UseNpgsql(connectionString, npgsqlOptions =>
{
    npgsqlOptions.EnableRetryOnFailure(
        maxRetryCount: 3,
        maxRetryDelay: TimeSpan.FromSeconds(5),
        errorCodesToAdd: null);
});
```

Rules:
- Always enable retry on failure for production configurations
- Transient errors (network blips, failovers) are retried automatically
- Non-transient errors (constraint violations, syntax errors) are NOT retried
- Set reasonable max delay to avoid blocking request threads

### Connection Pool Sizing

Default Npgsql pool settings are generally sufficient:
- Default pool size: 100 connections per DbContext instance
- For high-throughput services, tune via connection string: `Pooling=true;Maximum Pool Size=200`

Rules:
- Monitor `pg_stat_activity` for connection count in production
- Set pool size based on expected peak concurrent queries, not total QPS
- Each Fargate task has its own pool — multiply by task count for total DB connections

## Common Anti-Patterns

| Anti-Pattern | Problem | Fix |
|-------------|---------|-----|
| `.ToList()` before `.Where()` | Loads entire table into memory, filters in C# | Filter in query: `.Where().ToList()` |
| Missing `Include()` | Null navigation properties or N+1 via lazy loading | Always `.Include()` needed navigations |
| `FindAsync()` for aggregates | Doesn't include children | Use `FirstOrDefaultAsync` with `.Include()` |
| Global query filters forgotten | Soft-deleted records leak into results | Apply `HasQueryFilter()` in configuration |
| `SaveChanges()` in a loop | One transaction per iteration, poor performance | Batch changes, single `SaveChangesAsync()` |
| String interpolation in raw SQL | SQL injection vulnerability | Use `FromSqlInterpolated()` or parameterized queries |
| Ignoring `CancellationToken` | Long queries block even after client disconnects | Pass `ct` to all async EF Core methods |
| `DbContext` in singleton | Thread-safety violation, stale data | Always scoped lifetime, use `IServiceScopeFactory` in background services |

## Performance Monitoring

### Query Logging

In development, EF Core logs all generated SQL:

```json
{
  "Logging": {
    "LogLevel": {
      "Microsoft.EntityFrameworkCore.Database.Command": "Information"
    }
  }
}
```

### Identifying Slow Queries

- OpenTelemetry EF Core instrumentation tracks query duration automatically
- Set alerts on queries exceeding 100ms
- Use `EXPLAIN ANALYZE` in PostgreSQL for slow query investigation
- Add indexes for frequently filtered/sorted columns

### Index Strategy

```csharp
// In entity type configuration
builder.HasIndex(e => e.CustomerId);
builder.HasIndex(e => e.Status);
builder.HasIndex(e => new { e.Status, e.CreatedAt }); // Composite for filtered sorts
```

Rules:
- Add indexes for columns used in `WHERE`, `ORDER BY`, and `JOIN` clauses
- Don't over-index — each index slows down writes
- Use composite indexes for queries that filter and sort together
- Unique indexes enforce domain invariants at the database level
