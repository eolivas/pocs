---
inclusion: fileMatch
fileMatchPattern: "**/*Middleware*.cs,**/*Extension*.cs,**/Program.cs,**/observability/**"
---

# Logging Patterns & Best Practices

This project uses Serilog with structured logging, JSON output, and correlation ID enrichment. This document covers log levels, message templates, what to log, what to avoid, and configuration patterns.

## Logging Framework

| Component | Role |
|-----------|------|
| **Serilog** | Structured logging library |
| **Serilog.AspNetCore** | ASP.NET Core integration, request logging |
| **Console sink** | JSON output in containers (stdout for Docker/ECS/Container Apps) |
| **LogContext** | Ambient property enrichment (CorrelationId, UserId) |
| **OpenTelemetry** | Trace/span correlation via `TraceId` and `SpanId` |

## Log Levels

### When to Use Each Level

| Level | When | Examples |
|-------|------|---------|
| `Verbose` | Ultra-detailed debugging (never in production) | Variable values in loops, method entry/exit |
| `Debug` | Diagnostic info useful during development | Cache hit/miss, query parameters, config values loaded |
| `Information` | Significant business events (happy path) | Entity placed, user registered, consumer processed event |
| `Warning` | Unexpected but recoverable situations | Retry triggered, cache miss on expected key, deprecated API called |
| `Error` | Failures that affect the current operation | Unhandled exception in handler, DB connection failed, external API 500 |
| `Fatal` | Application-wide failure, process will terminate | Database unreachable on startup, unrecoverable configuration error |

### Rules

- **Production minimum level**: `Information` (suppress `Debug` and `Verbose`)
- **Development minimum level**: `Debug`
- `Information` should tell the story of what the system is doing at a business level
- `Warning` means "something unexpected happened but we handled it"
- `Error` means "this request/operation failed" — always include the exception
- `Fatal` is extremely rare — only for startup failures or unrecoverable states
- Never log at `Error` for expected conditions (e.g., entity not found → that's a 404, not an error)

## Structured Message Templates

### Use Message Templates, Not String Interpolation

```csharp
// GOOD: Structured — properties are indexed and queryable
_logger.LogInformation("Entity {EntityId} placed by customer {CustomerId}", entity.Id, customerId);

// BAD: String interpolation — destroys structure, can't query by field
_logger.LogInformation($"Entity {entity.Id} placed by customer {customerId}");

// BAD: String concatenation
_logger.LogInformation("Entity " + entity.Id + " placed by customer " + customerId);
```

### Template Rules

- Use PascalCase for property names in templates: `{EntityId}`, `{CustomerId}`, `{ElapsedMs}`
- Use `@` prefix for destructuring objects: `{@Request}` (serializes the full object)
- Use `$` prefix for stringification: `{$Status}` (calls `.ToString()`)
- Keep templates human-readable — they're the message; properties are the data
- One template per log call — don't build templates dynamically

### Naming Conventions for Properties

| Property | Convention | Example |
|----------|-----------|---------|
| Entity IDs | `{Entity}Id` | `{OrderId}`, `{CustomerId}`, `{UserId}` |
| Counts | `{Thing}Count` | `{LineCount}`, `{RetryCount}` |
| Durations | `{Operation}ElapsedMs` | `{HandlerElapsedMs}`, `{QueryElapsedMs}` |
| Status/State | `{Thing}Status` | `{EntityStatus}`, `{HealthStatus}` |
| Names | `{Thing}Name` | `{HandlerName}`, `{ConsumerName}` |

## What to Log

### Application Layer (Pipeline Behaviours)

```csharp
// LoggingBehaviour — already wired, logs every command/query
_logger.LogInformation("Handling {RequestName}", typeof(TRequest).Name);
_logger.LogInformation("Handled {RequestName} in {ElapsedMs}ms", typeof(TRequest).Name, elapsed);
```

### Domain Layer

Domain entities do NOT log. They raise domain events. Logging happens in the layers that orchestrate them.

### Infrastructure Layer

```csharp
// Consumer processing
_logger.LogInformation("Processing {EventType} for {EntityId}", nameof({Entity}PlacedEvent), context.Message.{Entity}Id);

// Outbox processor
_logger.LogInformation("Published {MessageCount} outbox messages in {ElapsedMs}ms", count, elapsed);
_logger.LogWarning("Outbox message {MessageId} failed, retry {RetryCount}/{MaxRetries}", id, retries, max);
_logger.LogError(ex, "Outbox message {MessageId} dead-lettered after {MaxRetries} retries", id, max);

// Repository (only for exceptional cases)
_logger.LogWarning("Entity {EntityId} not found", id); // Only if this is unexpected
```

### Api Layer

```csharp
// Middleware — already handled by Serilog.AspNetCore request logging
// Custom middleware
_logger.LogDebug("Correlation ID {CorrelationId} assigned to request", correlationId);
_logger.LogWarning("Rate limit exceeded for {PartitionKey}", partitionKey);
```

## What NOT to Log

### Never Log

| Category | Why | Example |
|----------|-----|---------|
| **Secrets** | Security breach | Passwords, API keys, tokens, connection strings |
| **PII** | GDPR/privacy compliance | Email addresses, phone numbers, full names |
| **Request/response bodies** | Performance + security | Full JSON payloads at `Information` level |
| **Successful routine operations at Warning/Error** | Alert fatigue | "Cache hit" at Warning, "Request completed" at Error |
| **Stack traces at Information** | Noise | Only include stack traces with Error/Fatal |

### Mask or Reference Instead

```csharp
// BAD: Logging PII
_logger.LogInformation("Sending email to {Email}", customer.Email);

// GOOD: Log a reference, not the value
_logger.LogInformation("Sending notification to customer {CustomerId}", customer.Id);

// BAD: Logging secrets
_logger.LogDebug("Connecting to {ConnectionString}", connectionString);

// GOOD: Log the target, not the credential
_logger.LogDebug("Connecting to database {DatabaseName} on {Host}", dbName, host);
```

## Correlation & Context Enrichment

### Automatic Enrichers

Every log entry automatically includes (via Serilog configuration and middleware):

| Property | Source | Purpose |
|----------|--------|---------|
| `CorrelationId` | `CorrelationIdMiddleware` → `LogContext` | Links all logs for one user request |
| `TraceId` | OpenTelemetry | Links logs to distributed traces |
| `SpanId` | OpenTelemetry | Links logs to specific operation spans |
| `RequestMethod` | Serilog.AspNetCore | HTTP method |
| `RequestPath` | Serilog.AspNetCore | URL path |
| `StatusCode` | Serilog.AspNetCore | HTTP response status |
| `Elapsed` | Serilog.AspNetCore | Request duration |

### Adding Custom Context

```csharp
// Push context for the duration of a scope
using (LogContext.PushProperty("CustomerId", customerId))
using (LogContext.PushProperty("EntityId", entityId))
{
    // All log entries within this scope include CustomerId and EntityId
    _logger.LogInformation("Processing entity");
    await DoWork();
}
```

### Correlation Flow

```
HTTP Request (CorrelationId assigned)
  → Handler logs (CorrelationId in context)
    → Repository call (CorrelationId in context)
    → OutboxMessage stores CorrelationId
      → OutboxProcessor publishes with X-Correlation-Id header
        → Consumer extracts header, pushes to LogContext
          → Consumer logs (same CorrelationId)
```

## Exception Logging

### Log at the Boundary, Not at Every Layer

```csharp
// BAD: Logging and rethrowing at every layer (duplicate log entries)
public class Repository
{
    public async Task Save(...)
    {
        try { await _db.SaveAsync(); }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save"); // Logged here
            throw; // AND logged again in the handler, AND in middleware
        }
    }
}

// GOOD: Let exceptions bubble up, log once at the boundary
// ExceptionHandlingMiddleware catches and logs unhandled exceptions
// Handlers only log if they handle the exception (don't rethrow)
```

### Rules

- Log exceptions at the outermost boundary (`ExceptionHandlingMiddleware`) — not at every catch
- If you catch and handle an exception (alternative path), log at `Warning`
- If you catch and rethrow, do NOT log — the boundary will log it
- Always use the exception overload: `_logger.LogError(ex, "Message {Param}", param)`
- Never swallow exceptions silently (empty catch block)

### Exception Logging Pattern

```csharp
// Middleware (boundary) — logs all unhandled exceptions
public async Task InvokeAsync(HttpContext context)
{
    try
    {
        await _next(context);
    }
    catch (ValidationException ex)
    {
        // Expected — log at Information, return 400
        _logger.LogInformation("Validation failed: {Errors}", ex.Errors);
        context.Response.StatusCode = 400;
    }
    catch ({Entity}DomainException ex)
    {
        // Business rule violation — log at Information, return 409
        _logger.LogInformation("Domain rule violated: {Message}", ex.Message);
        context.Response.StatusCode = 409;
    }
    catch (Exception ex)
    {
        // Unexpected — log at Error, return 500
        _logger.LogError(ex, "Unhandled exception processing {Method} {Path}",
            context.Request.Method, context.Request.Path);
        context.Response.StatusCode = 500;
    }
}
```

## Log Output Configuration

### Development (Console, human-readable)

```json
{
  "Serilog": {
    "MinimumLevel": {
      "Default": "Debug",
      "Override": {
        "Microsoft.AspNetCore": "Warning",
        "Microsoft.EntityFrameworkCore.Database.Command": "Information"
      }
    },
    "WriteTo": [{ "Name": "Console" }]
  }
}
```

### Production (JSON, machine-parseable)

```json
{
  "Serilog": {
    "MinimumLevel": {
      "Default": "Information",
      "Override": {
        "Microsoft.AspNetCore": "Warning",
        "Microsoft.EntityFrameworkCore": "Warning"
      }
    },
    "WriteTo": [{
      "Name": "Console",
      "Args": { "formatter": "Serilog.Formatting.Compact.CompactJsonFormatter, Serilog.Formatting.Compact" }
    }]
  }
}
```

### Level Overrides

| Namespace | Production Level | Reason |
|-----------|-----------------|--------|
| `Microsoft.AspNetCore` | Warning | Suppress noisy request/routing logs (Serilog handles request logging) |
| `Microsoft.EntityFrameworkCore` | Warning | Suppress query logs (use OpenTelemetry for query tracing) |
| `Microsoft.EntityFrameworkCore.Database.Command` | Warning | Only enable at Information for debugging slow queries |
| `MassTransit` | Information | Consumer lifecycle events are useful |
| `System.Net.Http.HttpClient` | Warning | Suppress individual HTTP request logs |

## Frontend Logging

### Error Reporting

```typescript
// Global error handler — report to monitoring service
window.addEventListener('unhandledrejection', (event) => {
  console.error('Unhandled promise rejection:', event.reason);
  // Send to Application Insights, Sentry, or similar
});
```

### API Error Logging

```typescript
// In the HTTP interceptor (lib/http.ts)
http.interceptors.response.use(
  (response) => response,
  (error) => {
    if (!error.response) {
      console.error('Network error: unable to reach server');
    } else if (error.response.status >= 500) {
      console.error('Server error:', error.response.status, error.response.data);
    }
    return Promise.reject(error);
  }
);
```

### Rules

- Use `console.error` for errors that need investigation
- Use `console.warn` for deprecation notices or unexpected fallbacks
- Never log sensitive user data (tokens, passwords, PII) to the browser console
- In production builds, consider stripping `console.debug` via Vite plugin
- Report unhandled errors to a monitoring service (Application Insights, Sentry)

## Performance Considerations

- Avoid logging inside tight loops — aggregate and log once
- Use `LogLevel` guards for expensive computations:

```csharp
if (_logger.IsEnabled(LogLevel.Debug))
{
    var expensiveData = ComputeDebugInfo(); // Only computed if Debug is enabled
    _logger.LogDebug("Debug info: {@Data}", expensiveData);
}
```

- Structured logging with message templates is faster than string interpolation (no allocation if level is suppressed)
- JSON sink serialization adds ~1μs per log entry — negligible for most workloads
- If logging throughput becomes a bottleneck, use `Serilog.Sinks.Async` to buffer writes
