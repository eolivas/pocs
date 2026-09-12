---
inclusion: manual
---

# Microservices Best Practices

This document defines the microservices conventions for this platform. All services follow Clean Architecture, communicate asynchronously via domain events, and deploy independently on ECS Fargate (AWS) or Container Apps (Azure).

## Service Boundaries

### Bounded Context Ownership

Each service owns exactly one bounded context:

| Service | Bounded Context | Owns |
|---------|----------------|------|
| Identity Service | User management, authentication | User aggregate, Role VO |
| {Entity} Service | Core business domain | {Entity} aggregate, {Entity}Line entity, Money VO |
| Notifications Service | Event-driven side effects | No aggregates — consumer only |
| MCP Gateway | AI tool orchestration | No domain — proxies to other services |
| BFF Service | Client aggregation | No domain — composes responses from other services |

Rules:
- A service MUST NOT directly access another service's database
- A service MUST NOT share domain models or entities across service boundaries
- Each service has its own database schema (logical isolation at minimum, physical isolation preferred)
- Cross-service communication is exclusively via domain events or synchronous API calls through the BFF

### Service Sizing

- Prefer larger, cohesive services over many tiny ones
- Split a service only when bounded contexts have genuinely different lifecycles, teams, or scaling requirements
- A good service has 1-3 aggregate roots and a clear domain boundary
- If two "services" always deploy together and share a database, they should be one service

## Inter-Service Communication

### Asynchronous (Preferred)

Use domain events via MassTransit for loose coupling:

```
{Entity} Service → publishes {Entity}PlacedEvent → SNS/SQS or Service Bus → Notifications Service consumes
```

Rules:
- Events are facts about what happened — past tense naming (`{Entity}PlacedEvent`, not `Place{Entity}Command`)
- Events carry only IDs and minimal data — consumers query back if they need more
- Producers have no knowledge of consumers (publish-subscribe)
- Consumers are idempotent — same event processed twice yields same result
- Events are delivered at-least-once via the outbox pattern (see ADR-004)

### Synchronous (When Necessary)

Use HTTP calls through the BFF for request/response patterns:

```
BFF → GET /api/{entities}/{id} → {Entity} Service
BFF → GET /api/users/me → Identity Service
```

Rules:
- Only the BFF and MCP Gateway make synchronous cross-service calls
- Services MUST NOT call each other directly (no service-to-service HTTP)
- Set timeouts on all HTTP clients (default: 5 seconds)
- Use circuit breakers for resilience (Polly or built-in `HttpClient` resilience)
- Cache responses where freshness allows (reduce coupling on availability)

### Communication Decision Matrix

| Scenario | Pattern | Why |
|----------|---------|-----|
| Notify downstream of state change | Async event | Loose coupling, no availability dependency |
| Compose response from multiple services | BFF sync call | Client needs aggregated data immediately |
| Validate against another service's data | Async event (replicate locally) | Avoid runtime coupling for validation |
| Trigger side effect (email, SMS) | Async event | Fire-and-forget, retry on failure |
| Real-time query of another service | BFF sync with cache | Freshness required, but with fallback |

## Data Ownership

### Database per Service

- Each service owns its database schema — no shared tables
- In production: separate RDS instances or schemas with distinct credentials
- In Docker Compose: single PostgreSQL instance with separate databases (logical isolation)
- Connection strings are per-service (`ConnectionStrings__{SolutionName}Db`)

### No Shared State

- No shared caches between services (each service has its own Redis namespace if needed)
- No shared message queues (each consumer gets its own queue via MassTransit conventions)
- No shared file storage (each service has its own S3 bucket/prefix if needed)

### Data Replication via Events

When a service needs data owned by another:
1. Subscribe to the relevant domain events
2. Build a local read model (projection) from those events
3. Query the local projection — never the source service's database

```csharp
// Notifications service builds a local customer-email lookup from Identity events
public sealed class UserRegisteredConsumer : IConsumer<UserRegisteredEvent>
{
    public Task Consume(ConsumeContext<UserRegisteredEvent> context)
    {
        // Store userId → email mapping locally for notification delivery
        return Task.CompletedTask;
    }
}
```

## Resilience Patterns

### Retry with Exponential Backoff

All external calls (HTTP, message broker) use retry with exponential backoff:

| Component | Retries | Backoff | Max Delay |
|-----------|---------|---------|-----------|
| MassTransit consumers | 3 | Exponential (1s → 8s) | 8s |
| HTTP clients | 3 | Exponential (1s → 4s) | 4s |
| Outbox processor | 5 (per message) | Polling interval (5s) | Dead-letter after max |

### Circuit Breaker

For synchronous HTTP calls from BFF/MCP Gateway:
- Open circuit after 5 consecutive failures
- Half-open after 30 seconds — allow one probe request
- Close circuit on successful probe

### Timeout

- HTTP client timeout: 5 seconds (configurable per client)
- Database query timeout: 30 seconds
- Health check timeout: 5 seconds per dependency

### Bulkhead Isolation

- Each HTTP client has its own `HttpClient` instance (via `IHttpClientFactory`)
- Failure in one downstream service does not exhaust connection pools for others
- MassTransit consumers run with configurable concurrency limits

### Dead Letter Queue (DLQ)

- Messages that fail after max retries are moved to `_error` suffix queues
- DLQ messages require manual investigation and replay
- Monitor DLQ depth with CloudWatch/Azure Monitor alarms

## Deployment Independence

### Independent Deployability

- Each service has its own Dockerfile, CI pipeline, and ECS/Container App task definition
- Deploying one service MUST NOT require deploying another
- API contracts between services are versioned — breaking changes require migration coordination
- Database migrations are per-service and run independently

### Versioning Strategy

- Services do not expose version numbers to each other
- API contracts are backward-compatible by default (additive changes only)
- Breaking changes follow the expand-contract pattern:
  1. **Expand**: Add new field/endpoint alongside old one
  2. **Migrate**: Move consumers to new contract
  3. **Contract**: Remove old field/endpoint

### Feature Flags

- Use configuration (`IConfiguration`) for feature toggles — not code branches
- Flag-gated code ships dark (disabled in production, enabled in staging)
- Remove flags within 2 sprints of full rollout

## Observability

### Distributed Tracing

- All services export traces via OpenTelemetry OTLP to the collector
- Correlation ID propagates from HTTP request → outbox → message consumer
- Trace context (`traceparent` header) follows W3C Trace Context standard
- Every service adds ASP.NET Core and EF Core instrumentation

### Structured Logging

- JSON-formatted logs (Serilog with `Console` sink in containers)
- Every log entry includes: `CorrelationId`, `ServiceName`, `TraceId`, `SpanId`
- Log levels: `Information` for business events, `Warning` for recoverable errors, `Error` for unrecoverable failures
- Never log PII (emails, tokens, passwords) — use masked references

### Health Checks

Every service exposes:
- `GET /health/live` — liveness (always 200, no dependency checks)
- `GET /health/ready` — readiness (checks database, message broker)

Rules:
- Orchestrators (ECS, Container Apps) use liveness for restart decisions
- Load balancers use readiness for routing decisions
- Health check endpoints are anonymous (no auth required)

### Metrics

- HTTP request rate, latency (p50, p95, p99), error rate per endpoint
- Outbox: messages processed, failed, duration
- Custom business metrics: domain events published, commands handled
- Alert on: error rate > 1%, p99 latency > SLA, DLQ depth > 0

## Security Between Services

### Service-to-Service Authentication

- BFF and MCP Gateway authenticate to downstream services via JWT Bearer tokens
- Tokens are machine-to-machine (client credentials flow)
- Each service validates `iss` (issuer) and `aud` (audience) claims
- No service trusts unsigned or expired tokens

### Network Isolation

- Services communicate over private subnets only
- No service exposes ports to the public internet (only through API Gateway/APIM)
- Security groups restrict traffic to required ports between specific services

### Secrets Management

- Connection strings, API keys, and credentials are injected from Secrets Manager / Key Vault
- Secrets are never stored in code, config files, or environment variable definitions in source control
- Secrets rotate on a schedule (90 days for credentials, annually for certificates)

## Anti-Patterns to Avoid

| Anti-Pattern | Why It's Bad | What to Do Instead |
|-------------|-------------|-------------------|
| Shared database | Tight coupling, cascading schema changes | Database per service |
| Direct service-to-service calls | Creates availability chains | Route through BFF or use events |
| Synchronous event publishing | Dual-write problem, data loss risk | Outbox pattern |
| God service (does everything) | Violates bounded contexts, scaling bottleneck | Split by business capability |
| Distributed monolith | Services can't deploy independently | Verify independent deployability |
| Chatty communication | Performance degradation, latency chains | Coarse-grained events, local projections |
| Shared domain models (NuGet packages) | Coupling disguised as reuse | Each service owns its types |
