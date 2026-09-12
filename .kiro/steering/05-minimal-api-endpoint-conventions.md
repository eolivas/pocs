---
inclusion: fileMatch
fileMatchPattern: "**/*Endpoint*.cs,**/*Module*.cs,**/Program.cs"
---

# Minimal API Endpoint Conventions

All HTTP endpoints are defined as Minimal APIs in `src/{SolutionName}.Api/Endpoints/`. Follow these conventions when adding new endpoints.

## Endpoint Group Structure

Each resource gets its own static class with a `Map{Resource}Endpoints` extension method:

```csharp
using MediatR;
using Microsoft.AspNetCore.Http.HttpResults;
using {SolutionName}.Application.Commands;
using {SolutionName}.Application.DTOs;
using {SolutionName}.Application.Queries;

namespace {SolutionName}.Api.Endpoints;

public static class InvoicesEndpoints
{
    public static IEndpointRouteBuilder MapInvoicesEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/invoices")
            .RequireAuthorization();

        // Define endpoints on `group`...

        return endpoints;
    }
}
```

Register in `Program.cs`:
```csharp
app.Map{Entity}Endpoints();
app.MapInvoicesEndpoints(); // Add new resource
```

## Rate Limiting

Endpoint groups that handle public traffic should apply rate limiting:

```csharp
var group = endpoints.MapGroup("/api/{entities}")
    .RequireAuthorization()
    .RequireRateLimiting("{solution-name}-api");  // Fixed-window rate limit
```

The `"{solution-name}-api"` rate limit policy is configured in `Add{SolutionName}RateLimiter()` extension. When exceeded, returns HTTP 429 with `Retry-After` header.

## Health Check & Operational Endpoints

These are registered separately from business endpoints:

| Endpoint | Purpose | Auth |
|----------|---------|------|
| `GET /health/live` | Liveness probe (always healthy) | Anonymous |
| `GET /health/ready` | Readiness (checks PostgreSQL + RabbitMQ) | Anonymous |
| `GET /openapi/v1.json` | OpenAPI spec | Anonymous |
| `GET /swagger` | Swagger UI (Development only) | Anonymous |

Do NOT add auth or rate limiting to health check endpoints.

## Endpoint Conventions

### Route Pattern
- Base: `/api/{resource}` (plural, lowercase)
- Item: `/api/{resource}/{id:guid}`
- Actions: `/api/{resource}/{id:guid}/{action}`

### Required Metadata
Every endpoint MUST include:
```csharp
group.MapPost("/", async (...) => { ... })
    .WithName("Place{Entity}")           // Unique operation name (PascalCase)
    .WithSummary("Places a new {entity}") // Short description
    .Produces(StatusCodes.Status201Created)
    .Produces(StatusCodes.Status400BadRequest)
    .WithOpenApi();                    // OpenAPI metadata generation
```

### Authorization
- `RequireAuthorization()` on the group (all endpoints require auth by default)
- For public endpoints, override with `.AllowAnonymous()`

### Dispatching via MediatR
Inject `ISender sender` (not `IMediator`) and dispatch:
```csharp
group.MapGet("/{id:guid}", async (Guid id, ISender sender) =>
{
    var result = await sender.Send(new Get{Entity}Query(id));
    return result is not null ? Results.Ok(result) : Results.NotFound();
});
```

## HTTP Status Code Conventions

| Operation | Success | Client Error | Conflict |
|-----------|---------|--------------|----------|
| POST (create) | 201 Created | 400 Bad Request | 409 Conflict |
| GET (single) | 200 OK | — | — |
| GET (list) | 200 OK | — | — |
| PUT (update) | 200 OK or 204 No Content | 400 Bad Request | 409 Conflict |
| DELETE (soft) | 204 No Content | — | 409 Conflict |

- Return `Results.NotFound()` when a requested entity does not exist
- Return `Results.Conflict()` when a domain exception indicates invalid state transition

## Request/Response Records

Co-locate request/response records at the bottom of the endpoint file:

```csharp
public record Place{Entity}Request
{
    public Guid CustomerId { get; init; }
    public IReadOnlyList<Place{Entity}LineRequest> Lines { get; init; } = [];
}

public record Place{Entity}LineRequest
{
    public Guid ProductId { get; init; }
    public int Quantity { get; init; }
    public decimal UnitPrice { get; init; }
    public string Currency { get; init; } = string.Empty;
}

public record Cancel{Entity}Request
{
    public string Reason { get; init; } = string.Empty;
}
```

Rules:
- Use `record` types with `init` properties
- Request suffix: `{Verb}{Noun}Request`
- These are API-level DTOs, distinct from Application-layer DTOs

## Error Handling

Domain exceptions are caught in the endpoint or by `ExceptionHandlingMiddleware`:
```csharp
try
{
    await sender.Send(command);
    return Results.NoContent();
}
catch ({Entity}DomainException)
{
    return Results.Conflict();
}
```

`ValidationException` from FluentValidation is handled by the middleware and returns 400.


## Endpoint Filters

Endpoint filters are the Minimal API equivalent of action filters. Use them for cross-cutting concerns scoped to specific endpoints or groups.

### Request Validation Filter

```csharp
public class ValidationFilter<TRequest> : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var request = context.Arguments.OfType<TRequest>().FirstOrDefault();
        if (request is null)
            return Results.BadRequest();

        // Custom validation logic or delegate to FluentValidation
        return await next(context);
    }
}

// Registration
group.MapPost("/", handler)
    .AddEndpointFilter<ValidationFilter<Place{Entity}Request>>();
```

### Logging Filter

```csharp
public class RequestLoggingFilter : IEndpointFilter
{
    private readonly ILogger<RequestLoggingFilter> _logger;

    public RequestLoggingFilter(ILogger<RequestLoggingFilter> logger) { _logger = logger; }

    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        _logger.LogDebug("Executing endpoint {DisplayName}", context.HttpContext.GetEndpoint()?.DisplayName);

        var result = await next(context);

        _logger.LogDebug("Executed endpoint {DisplayName}", context.HttpContext.GetEndpoint()?.DisplayName);
        return result;
    }
}
```

### Rules

- Filters run in registration order (outermost first)
- Use filters for endpoint-specific cross-cutting concerns
- Prefer pipeline behaviours (MediatR) for handler-level concerns (validation, logging)
- Filters can short-circuit by returning a result without calling `next(context)`
- Filters can access DI services via constructor injection

## Route Group Shared Conventions

Apply shared metadata to all endpoints in a group:

```csharp
public static IEndpointRouteBuilder Map{Entity}Endpoints(this IEndpointRouteBuilder endpoints)
{
    var group = endpoints.MapGroup("/api/{entities}")
        .RequireAuthorization()
        .RequireRateLimiting("{solution-name}-api")
        .WithTags("{Entities}")                    // OpenAPI tag grouping
        .WithOpenApi()                             // Applied to all in group
        .AddEndpointFilter<RequestLoggingFilter>(); // Shared filter

    group.MapGet("/", ListHandler);
    group.MapGet("/{id:guid}", GetByIdHandler);
    group.MapPost("/", CreateHandler);
    group.MapPut("/{id:guid}", UpdateHandler);
    group.MapDelete("/{id:guid}", DeleteHandler);
    group.MapPost("/{id:guid}/cancel", CancelHandler);

    return endpoints;
}
```

### Nested Groups

For sub-resources or versioned APIs:

```csharp
var v1 = endpoints.MapGroup("/api/v1");
var entities = v1.MapGroup("/{entities}").RequireAuthorization();
var admin = v1.MapGroup("/admin").RequireAuthorization("AdminPolicy");
```

Rules:
- Group-level conventions (auth, rate limiting, tags) apply to all child endpoints
- Per-endpoint overrides (`.AllowAnonymous()`) take precedence over group settings
- Use `WithTags()` for logical OpenAPI grouping in Swagger UI

## File Uploads & Multipart Handling

```csharp
group.MapPost("/documents", async (IFormFile file, ISender sender) =>
{
    if (file.Length == 0)
        return Results.BadRequest("File is empty");

    if (file.Length > 10 * 1024 * 1024) // 10 MB limit
        return Results.BadRequest("File exceeds maximum size");

    var allowedTypes = new[] { "application/pdf", "image/png", "image/jpeg" };
    if (!allowedTypes.Contains(file.ContentType))
        return Results.BadRequest("Unsupported file type");

    using var stream = file.OpenReadStream();
    var command = new UploadDocumentCommand(file.FileName, file.ContentType, stream);
    var id = await sender.Send(command);

    return Results.Created($"/api/documents/{id}", new { id });
})
.DisableAntiforgery() // Required for multipart in Minimal APIs
.Accepts<IFormFile>("multipart/form-data")
.Produces(StatusCodes.Status201Created)
.Produces(StatusCodes.Status400BadRequest);
```

### Rules

- Always validate file size before processing (reject early)
- Whitelist allowed content types — never trust client-provided MIME type blindly
- Stream files to storage — never load entire file into memory for large uploads
- Set explicit size limits (middleware rejects bodies > 1 MB by default; override per-endpoint if needed)
- Add `.DisableAntiforgery()` for multipart endpoints
- Declare `.Accepts<IFormFile>("multipart/form-data")` for OpenAPI documentation

## Output Caching (.NET 8+)

For read-heavy endpoints that can tolerate stale data:

```csharp
group.MapGet("/", ListHandler)
    .CacheOutput(policy => policy
        .Expire(TimeSpan.FromSeconds(30))
        .Tag("{entities}"));

group.MapGet("/{id:guid}", GetByIdHandler)
    .CacheOutput(policy => policy
        .Expire(TimeSpan.FromMinutes(5))
        .SetVaryByRouteValue("id")
        .Tag("{entities}"));
```

### Cache Invalidation

```csharp
group.MapPost("/", async (Place{Entity}Request request, ISender sender, IOutputCacheStore cache) =>
{
    var id = await sender.Send(command);

    // Invalidate cached list after creation
    await cache.EvictByTagAsync("{entities}", CancellationToken.None);

    return Results.Created($"/api/{entities}/{id.Value}", new { id = id.Value });
});
```

### Rules

- Only cache GET endpoints — never cache mutations
- Use tags for grouped invalidation (evict all entities when one changes)
- Set `VaryByRouteValue` for item endpoints (different cache per ID)
- Set `VaryByQuery` for filtered lists (different cache per filter combination)
- Keep TTLs short for frequently changing data (10-60 seconds)
- Authenticated endpoints: use `VaryByHeader("Authorization")` or disable caching
- Output caching is server-side — distinct from client-side `Cache-Control` headers

## Response Compression

Configured globally in `Program.cs`:

```csharp
builder.Services.AddResponseCompression(options =>
{
    options.EnableForHttps = true;
    options.Providers.Add<BrotliCompressionProvider>();
    options.Providers.Add<GzipCompressionProvider>();
});

app.UseResponseCompression(); // Before static files and endpoints
```

### Rules

- Enable for HTTPS in APIs behind a trusted reverse proxy (CloudFront/Front Door handles TLS)
- Brotli preferred (better compression ratio); Gzip as fallback
- Don't compress responses < 1 KB (overhead exceeds benefit)
- Don't compress already-compressed content (images, videos)
- Place `UseResponseCompression()` early in the pipeline (before routing)

## Dependency Injection in Endpoints

Minimal API handlers resolve dependencies from the DI container via parameters:

```csharp
// Parameters resolved from DI
group.MapPost("/", async (
    Place{Entity}Request request,       // From request body (JSON)
    ISender sender,                      // From DI
    IOutputCacheStore cache,             // From DI
    CancellationToken ct                 // From framework
) =>
{
    // ...
});
```

### Parameter Binding Sources

| Source | How | Example |
|--------|-----|---------|
| Route | `{id:guid}` in template | `Guid id` |
| Query string | `?page=2` | `int page = 1` |
| Request body | JSON deserialization | `Place{Entity}Request request` |
| DI container | Registered service | `ISender sender` |
| Headers | `[FromHeader]` attribute | `[FromHeader(Name = "X-Correlation-Id")] string? correlationId` |
| Framework | Special types | `HttpContext context`, `CancellationToken ct` |

### Rules

- Inject `ISender` (not `IMediator`) — narrower interface, only send capability
- Always accept `CancellationToken` — propagate to all async operations
- Use default parameter values for optional query parameters: `int page = 1, int pageSize = 20`
- Complex query filters: use `[AsParameters]` with a record for clean binding
