---
inclusion: manual
---

# RESTful API Best Practices

This document covers REST resource design, HTTP semantics, error handling, pagination, versioning, caching, and integration patterns. For project-specific endpoint scaffolding (groups, MediatR dispatch, rate limiting), see `05-minimal-api-endpoint-conventions.md`.

## Resource Naming

### URL Design Principles

| Principle | Rule | Example |
|-----------|------|---------|
| Nouns, not verbs | Resources are things, not actions | `/api/leadsmicroservice` not `/api/getLeadsMicroservice` |
| Plural | Collections use plural nouns | `/api/customers` not `/api/customer` |
| Lowercase | URL paths are case-insensitive by convention | `/api/order-lines` not `/api/OrderLines` |
| Hyphens for readability | Multi-word resources use hyphens | `/api/order-lines` not `/api/order_lines` |
| Hierarchical relationships | Nest when parent-child is strong | `/api/leadsmicroservice/{id}/lines` |
| Flat when independent | Don't over-nest | `/api/products` not `/api/categories/{id}/products` |

### URL Patterns

```
GET    /api/{resource}              → List (collection)
GET    /api/{resource}/{id}         → Get single (item)
POST   /api/{resource}              → Create
PUT    /api/{resource}/{id}         → Full replace
PATCH  /api/{resource}/{id}         → Partial update
DELETE /api/{resource}/{id}         → Remove

POST   /api/{resource}/{id}/{action} → Non-CRUD action (e.g., /api/leadsmicroservice/{id}/cancel)
```

### Rules

- Use GUIDs for resource IDs in URLs: `/api/leadsmicroservice/{id:guid}`
- Avoid exposing internal/sequential IDs — they leak information about volume
- Actions that don't map to CRUD use `POST` with a verb sub-resource: `/api/leadsmicroservice/{id}/place`
- Query parameters for filtering/sorting: `/api/leadsmicroservice?status=placed&sort=-createdAt`
- Never put verbs in the URL path for standard CRUD operations

## HTTP Method Semantics

| Method | Semantics | Idempotent | Safe | Request Body |
|--------|-----------|:----------:|:----:|:------------:|
| GET | Retrieve resource(s) | Yes | Yes | No |
| POST | Create resource or trigger action | No | No | Yes |
| PUT | Full replace of resource | Yes | No | Yes |
| PATCH | Partial update of resource | Yes* | No | Yes |
| DELETE | Remove resource | Yes | No | No |
| OPTIONS | Describe communication options | Yes | Yes | No |
| HEAD | Same as GET but no body | Yes | Yes | No |

*PATCH is idempotent when using JSON Merge Patch (same patch applied twice = same result).

### Rules

- **GET** MUST NOT modify state — it's safe and cacheable
- **POST** is the only non-idempotent method — use for creation and complex actions
- **PUT** replaces the entire resource — client sends full representation
- **PATCH** updates specific fields — prefer JSON Merge Patch (RFC 7396)
- **DELETE** returns 204 on success, 404 if already gone (idempotent)
- Never use GET for operations that change state (side effects in query strings)

## Response Status Codes

### Success Codes

| Operation | Status | When | Response Body |
|-----------|--------|------|---------------|
| POST (create) | 201 Created | Resource created | Created resource + `Location` header |
| GET (single) | 200 OK | Resource found | Resource representation |
| GET (list) | 200 OK | Collection returned (even if empty) | Array + pagination metadata |
| PUT | 200 OK | Updated and returning resource | Updated resource |
| PUT | 204 No Content | Updated, no body needed | None |
| PATCH | 200 OK | Partial update applied | Updated resource |
| DELETE | 204 No Content | Resource removed | None |

### Client Error Codes

| Status | When | Response Body |
|--------|------|---------------|
| 400 Bad Request | Validation failure, malformed input | ProblemDetails with `errors` dictionary |
| 401 Unauthorized | Missing or invalid authentication | ProblemDetails |
| 403 Forbidden | Authenticated but insufficient permissions | ProblemDetails |
| 404 Not Found | Resource does not exist | ProblemDetails |
| 409 Conflict | Domain rule violation, state conflict | ProblemDetails with explanation |
| 413 Content Too Large | Request body exceeds size limit | ProblemDetails |
| 422 Unprocessable Entity | Syntactically valid but semantically wrong | ProblemDetails |
| 429 Too Many Requests | Rate limit exceeded | ProblemDetails + `Retry-After` header |

### Server Error Codes

| Status | When | Response Body |
|--------|------|---------------|
| 500 Internal Server Error | Unhandled exception | Generic ProblemDetails (no internal details) |
| 502 Bad Gateway | Upstream service failure | ProblemDetails |
| 503 Service Unavailable | System overloaded or in maintenance | ProblemDetails + `Retry-After` header |

## Error Response Format (ProblemDetails — RFC 9457)

All error responses use the ProblemDetails standard:

```json
{
  "type": "https://tools.ietf.org/html/rfc9110#section-15.5.1",
  "title": "Validation Failed",
  "status": 400,
  "detail": "One or more validation errors occurred.",
  "instance": "/api/leadsmicroservice",
  "errors": {
    "lines": ["A order must contain at least one line."],
    "customerId": ["Customer ID is required."]
  }
}
```

### Rules

- Always return `application/problem+json` content type for errors
- `type` — URI reference identifying the problem type (use RFC section for standard HTTP errors)
- `title` — short human-readable summary (stable across instances)
- `status` — HTTP status code (duplicated for convenience)
- `detail` — human-readable explanation specific to this occurrence
- `instance` — URI of the specific request that caused the error
- `errors` — extension field for per-field validation errors (dictionary of field → messages)
- Never expose stack traces, internal paths, or connection strings in error responses

## Pagination

### Offset-Based Pagination (Default)

```
GET /api/leadsmicroservice?page=2&pageSize=20
```

Response includes pagination metadata:

```json
{
  "data": [...],
  "pagination": {
    "page": 2,
    "pageSize": 20,
    "totalCount": 147,
    "totalPages": 8,
    "hasNextPage": true,
    "hasPreviousPage": true
  }
}
```

### Keyset Pagination (High-Volume)

For large datasets where offset becomes expensive:

```
GET /api/leadsmicroservice?after=01234567-89ab-cdef&limit=20
```

Rules:
- Default page size: 20
- Maximum page size: 100 (reject requests above this)
- Always include `totalCount` for offset pagination (unless prohibitively expensive)
- Use keyset pagination when tables exceed 100K rows
- Always require explicit `OrderBy` — results without ordering are non-deterministic

## Filtering & Sorting

### Filtering via Query Parameters

```
GET /api/leadsmicroservice?status=placed&customerId=abc123
GET /api/leadsmicroservice?createdAfter=2025-01-01&createdBefore=2025-12-31
```

Rules:
- Use field name as query parameter: `?status=placed`
- Date ranges: `?createdAfter=...&createdBefore=...`
- Multiple values: `?status=placed&status=shipped` (OR logic)
- Don't invent a custom query language — keep it simple
- Validate filter values — return 400 for invalid enum values or dates

### Sorting

```
GET /api/leadsmicroservice?sort=createdAt        → ascending
GET /api/leadsmicroservice?sort=-createdAt       → descending (prefix with -)
GET /api/leadsmicroservice?sort=-createdAt,status → multiple sort fields
```

Rules:
- Prefix with `-` for descending
- Comma-separated for multiple fields
- Validate sort field names — return 400 for unknown fields
- Always have a default sort (e.g., `-createdAt`) when none specified

## API Versioning

### Strategy: URL Path Versioning

```
/api/v1/leadsmicroservice
/api/v2/leadsmicroservice
```

### When to Version

- **Breaking changes** (field removal, type change, semantic change) → new version
- **Additive changes** (new optional field, new endpoint) → same version (backward-compatible)

### Rules

- Start with `/api/v1/` from day one (even if you never make v2)
- Maintain previous version for minimum 6 months after new version launches
- Document deprecation timeline in API changelog
- Version the URL path — it's explicit and discoverable (no hidden headers)
- Internal service-to-service APIs (BFF → domain service) may skip versioning if deployed together

## Caching

### Response Headers

```csharp
// Immutable resources (never change once created)
group.MapGet("/{id:guid}", handler)
    .CacheOutput(p => p.Expire(TimeSpan.FromHours(1)).Tag("leadsmicroservice"));

// Volatile resources (change frequently)
// Use ETag for conditional requests
```

### ETag & Conditional Requests

```
// Response includes ETag
HTTP/1.1 200 OK
ETag: "a1b2c3d4"

// Subsequent request includes If-None-Match
GET /api/leadsmicroservice/123
If-None-Match: "a1b2c3d4"

// If unchanged → 304 Not Modified (no body transferred)
HTTP/1.1 304 Not Modified
```

### Cache-Control Guidelines

| Resource Type | Cache-Control | Example |
|---------------|---------------|---------|
| Static reference data | `public, max-age=3600` | Product catalog |
| User-specific mutable data | `private, no-cache` | User's leadsmicroservice |
| Frequently changing lists | `private, max-age=10` | Order status dashboard |
| Never cache | `no-store` | Authentication responses |

### Rules

- Set `Cache-Control` headers explicitly — don't rely on browser defaults
- Use `ETag` for resources that change but are read-heavy (avoids transferring unchanged data)
- Private APIs behind authentication should use `private` directive
- Rate-limited endpoints benefit from short client-side caching (reduces server load)

## Content Negotiation

### Request Content Type

```
POST /api/leadsmicroservice
Content-Type: application/json
```

### Response Content Type

```
Accept: application/json
```

Rules:
- This project uses `application/json` exclusively for request and response bodies
- Error responses use `application/problem+json`
- If a client sends unsupported `Content-Type`, return 415 Unsupported Media Type
- If a client requests unsupported `Accept`, return 406 Not Acceptable

## Partial Updates (PATCH)

### JSON Merge Patch (RFC 7396) — Preferred

Client sends only the fields to update:

```
PATCH /api/leadsmicroservice/123
Content-Type: application/merge-patch+json

{
  "status": "cancelled",
  "cancellationReason": "Customer request"
}
```

Rules:
- Omitted fields remain unchanged
- `null` explicitly removes a field (if nullable)
- Simpler than JSON Patch (RFC 6902) — prefer for most cases
- Validate the patch against domain rules after applying

## Bulk Operations

When clients need to operate on multiple resources:

```
POST /api/leadsmicroservice/batch
Content-Type: application/json

{
  "operations": [
    { "action": "cancel", "id": "abc-123", "reason": "Out of stock" },
    { "action": "cancel", "id": "def-456", "reason": "Out of stock" }
  ]
}
```

Response with per-item status:

```json
{
  "results": [
    { "id": "abc-123", "status": 204 },
    { "id": "def-456", "status": 409, "error": "Already shipped" }
  ]
}
```

Rules:
- Limit batch size (e.g., max 100 operations per request)
- Return partial success — don't fail the entire batch for one item
- Use `207 Multi-Status` if individual items have different outcomes
- Bulk operations are NOT atomic unless explicitly documented

## Consuming External REST APIs (Integration)

### HttpClient Best Practices

```csharp
// Register typed HTTP client with resilience
builder.Services.AddHttpClient<IExternalPaymentClient, ExternalPaymentClient>(client =>
{
    client.BaseAddress = new Uri(configuration["Payment:BaseUrl"]!);
    client.Timeout = TimeSpan.FromSeconds(5);
})
.AddStandardResilienceHandler(); // Polly retry + circuit breaker
```

### Rules

- Use `IHttpClientFactory` — never `new HttpClient()` (socket exhaustion)
- Set explicit timeouts per client (default 5 seconds)
- Add retry with exponential backoff for transient failures (5xx, timeout)
- Add circuit breaker to avoid cascading failures
- Log request/response at `Debug` level, errors at `Warning`
- Map external DTOs to internal domain types at the integration boundary
- Never expose external API models to your domain or application layer

### Error Handling for External APIs

```csharp
public async Task<PaymentResult> ProcessPaymentAsync(PaymentRequest request, CancellationToken ct)
{
    var response = await _httpClient.PostAsJsonAsync("/payments", request, ct);

    return response.StatusCode switch
    {
        HttpStatusCode.OK => await response.Content.ReadFromJsonAsync<PaymentResult>(ct),
        HttpStatusCode.BadRequest => throw new PaymentValidationException(await ReadProblemDetails(response)),
        HttpStatusCode.ServiceUnavailable => throw new PaymentUnavailableException(),
        _ => throw new PaymentIntegrationException($"Unexpected status: {response.StatusCode}")
    };
}
```

Rules:
- Handle each expected status code explicitly — don't assume 200
- Wrap external failures in domain-specific exceptions
- Retry only on transient failures (5xx, timeouts) — never retry 4xx
- Include correlation ID in outbound requests for end-to-end tracing

## OpenAPI Documentation

### Conventions

Every endpoint MUST include OpenAPI metadata:

```csharp
group.MapPost("/", handler)
    .WithName("PlaceOrder")
    .WithSummary("Places a new order")
    .WithDescription("Creates an order with the specified lines and transitions it to Placed status.")
    .Produces<{Entity}Dto>(StatusCodes.Status201Created)
    .Produces<ProblemDetails>(StatusCodes.Status400BadRequest)
    .Produces<ProblemDetails>(StatusCodes.Status409Conflict)
    .WithOpenApi();
```

### Rules

- `WithName` — unique operation ID (PascalCase, used for client generation)
- `WithSummary` — one-line description (shown in Swagger UI)
- `WithDescription` — detailed explanation (optional, for complex operations)
- `Produces<T>` — declare all possible response types and status codes
- Generate OpenAPI spec at `/openapi/v1.json` — keep it always up to date
- Use the OpenAPI spec as the source of truth for API consumers
- Client SDKs can be auto-generated from the spec (NSwag, OpenAPI Generator)
