---
inclusion: fileMatch
fileMatchPattern: "**/*Middleware*.cs,**/*Extension*.cs,**/Program.cs,**/observability/**"
---

# Middleware, Security & Observability

This project uses a layered middleware pipeline with security headers, correlation IDs, rate limiting, and OpenTelemetry. All middleware lives in `src/{SolutionName}.Api/Middleware/` and extensions in `src/{SolutionName}.Api/Extensions/`.

## Middleware Pipeline Order

The middleware is registered in `Program.cs` in this exact order (order matters):

```
SecurityHeaders → CorrelationId → ExceptionHandler → CORS → RateLimiting → Authentication → Authorization → Endpoint Routing
```

When adding new middleware, place it according to its cross-cutting scope:
- **Before CORS**: Security/headers that must appear on ALL responses (including CORS preflight, errors)
- **After CORS, before auth**: Rate limiting (applies to authenticated and unauthenticated requests)
- **After auth**: Business logic middleware

## Correlation ID Middleware

`CorrelationIdMiddleware` propagates a unique request identifier:

1. Extracts `X-Correlation-Id` header from request (must be a valid GUID)
2. If missing/invalid: generates a new GUID
3. Pushes `CorrelationId` to Serilog `LogContext` for the request duration
4. Sets `X-Correlation-Id` on response via `OnStarting` callback

All log entries during a request include the correlation ID. It flows through the outbox and into MassTransit message consumers.

## Security Headers Middleware

`SecurityHeadersMiddleware` adds OWASP-recommended headers on every response:

| Header | Value | Condition |
|--------|-------|-----------|
| `X-Content-Type-Options` | `nosniff` | Always |
| `X-Frame-Options` | `DENY` | Always |
| `Server` | (removed) | Always |
| `Strict-Transport-Security` | `max-age=31536000; includeSubDomains` | HTTPS only |

## Rate Limiting

Configured via `Add{SolutionName}RateLimiter()` extension:

- **Algorithm**: Fixed-window (`Microsoft.AspNetCore.RateLimiting`)
- **Policy name**: `"{solution-name}-api"` — applied to `/api/{entities}` group
- **Partition key**: `sub` claim (authenticated) or `RemoteIpAddress` (unauthenticated)
- **On success**: `X-RateLimit-Limit` and `X-RateLimit-Remaining` response headers
- **On rejection**: HTTP 429 with `Retry-After` header (seconds until window resets)

Configuration:
```json
{
  "RateLimit": {
    "PermitLimit": 100,
    "WindowSeconds": 60
  }
}
```

## CORS

Configured via `AddCorsPolicy()` extension. Reads allowed origins from configuration:

```json
{
  "Cors": {
    "AllowedOrigins": ["http://localhost:3000"]
  }
}
```

- Methods: GET, POST, PUT, DELETE, OPTIONS
- Headers: Authorization, Content-Type, X-Correlation-Id
- Credentials: allowed
- Preflight max-age: 600 seconds

## Health Checks

Registered in `Program.cs`:

```csharp
builder.Services.AddHealthChecks()
    .AddNpgSql(connectionString, timeout: TimeSpan.FromSeconds(5), tags: ["ready"])
    .AddRabbitMQ(rabbitUri, timeout: TimeSpan.FromSeconds(5), tags: ["ready"]);
```

Endpoints:
- `GET /health/live` → always 200 (liveness, no dependency checks)
- `GET /health/ready` → 200 only when PostgreSQL + RabbitMQ respond within 5s

Both return JSON with `status` and `entries`. No auth required.

## OpenTelemetry

Configured with both tracing and metrics:

```csharp
builder.Services.AddOpenTelemetry()
    .WithTracing(tp => tp
        .AddAspNetCoreInstrumentation()
        .AddEntityFrameworkCoreInstrumentation()
        .AddOtlpExporter())
    .WithMetrics(mp => mp
        .AddAspNetCoreInstrumentation()
        .AddMeter("{SolutionName}.Metrics")
        .AddOtlpExporter());
```

- Exports to OTEL Collector via OTLP gRPC (port 4317)
- Non-fatal: if collector is unreachable, API continues serving (telemetry dropped)
- Custom meters: register with `new Meter("{SolutionName}.Metrics")` or similar namespaced names
- Outbox metrics: `outbox.messages.processed`, `outbox.messages.failed`, `outbox.message.duration_ms`

## Input Validation

Request validation happens at two levels:

1. **Request body size**: Middleware rejects bodies > 1 MB with HTTP 413 (before deserialization)
2. **FluentValidation**: `Place{Entity}CommandValidator` runs as `IPipelineBehavior` in MediatR pipeline, returns 400 with ProblemDetails `errors` dictionary

Malformed JSON is caught by ASP.NET Core's model binding and returns 400 with ProblemDetails.

## Adding New Middleware

1. Create class in `src/{SolutionName}.Api/Middleware/`:

```csharp
public class MyCustomMiddleware
{
    private readonly RequestDelegate _next;

    public MyCustomMiddleware(RequestDelegate next) => _next = next;

    public async Task InvokeAsync(HttpContext context)
    {
        // Pre-processing
        await _next(context);
        // Post-processing
    }
}
```

2. Register in `Program.cs` in the correct pipeline position
3. Write property-based tests validating universal behavior (e.g., "for any request, header X is always present")

## Extension Method Pattern

Each cross-cutting concern has its own `IServiceCollection` extension in `Extensions/`:

```csharp
public static class CorsServiceCollectionExtensions
{
    public static IServiceCollection AddCorsPolicy(this IServiceCollection services, IConfiguration configuration)
    {
        // ...
        return services;
    }
}
```

This keeps `Program.cs` readable — one line per concern.
