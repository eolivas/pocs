---
inclusion: manual
---

# Security & Authentication Patterns

This document covers authentication, authorization, secure coding practices, and vulnerability management. For security headers, rate limiting, and CORS configuration, see `11-middleware-security-observability.md`. For secrets management, see `21-configuration-options-pattern.md`.

## Authentication (JWT Bearer)

### Configuration

```csharp
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.Authority = configuration["Jwt:Authority"];
        options.Audience = configuration["Jwt:Audience"];
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ClockSkew = TimeSpan.FromSeconds(30) // Tight clock skew (default is 5 min)
        };
    });
```

### Rules

- Always validate `iss` (issuer), `aud` (audience), `exp` (expiry), and signature
- Set `ClockSkew` to 30 seconds maximum (default 5 minutes is too generous)
- Never disable certificate validation (`RequireHttpsMetadata = false`) in production
- Use OIDC discovery (`.well-known/openid-configuration`) for automatic key rotation
- Tokens are validated on every request — no session state on the server

### Token Claims

| Claim | Purpose | Used For |
|-------|---------|----------|
| `sub` | Subject (user ID) | Rate limiting partition, audit logging |
| `email` | User email | Display only — never use as ID |
| `roles` | Role membership | Role-based authorization |
| `permissions` | Fine-grained permissions | Policy-based authorization |
| `iss` | Token issuer | Validation |
| `aud` | Intended audience | Validation |
| `exp` | Expiration time | Reject expired tokens |

## Authorization

### Endpoint-Level Authorization

```csharp
// All endpoints in group require auth (default)
var group = endpoints.MapGroup("/api/{entities}")
    .RequireAuthorization();

// Specific endpoint requires a policy
group.MapDelete("/{id:guid}", DeleteHandler)
    .RequireAuthorization("AdminOnly");

// Public endpoint overrides group auth
group.MapGet("/public-stats", StatsHandler)
    .AllowAnonymous();
```

### Policy-Based Authorization

```csharp
// Registration in Program.cs
builder.Services.AddAuthorizationBuilder()
    .AddPolicy("AdminOnly", policy => policy.RequireRole("Admin"))
    .AddPolicy("CanPlace{Entity}", policy => policy.RequireClaim("permissions", "entities:place"))
    .AddPolicy("OwnerOrAdmin", policy => policy.AddRequirements(new OwnerOrAdminRequirement()));
```

### Custom Authorization Handler

```csharp
public class OwnerOrAdminRequirement : IAuthorizationRequirement { }

public class OwnerOrAdminHandler : AuthorizationHandler<OwnerOrAdminRequirement, {Entity}>
{
    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        OwnerOrAdminRequirement requirement,
        {Entity} resource)
    {
        var userId = context.User.FindFirstValue("sub");

        if (context.User.IsInRole("Admin") || resource.CustomerId.Value.ToString() == userId)
        {
            context.Succeed(requirement);
        }

        return Task.CompletedTask;
    }
}
```

### Authorization Rules

- Default: all endpoints require authentication (`RequireAuthorization()` on group)
- Public endpoints MUST explicitly declare `.AllowAnonymous()` with justification in PR
- Prefer policy-based over role-based authorization (more flexible, composable)
- Resource-based authorization (owner check) uses `IAuthorizationService` in handlers
- Never trust client-supplied user ID — extract from the validated JWT `sub` claim

### Extracting User Context in Handlers

```csharp
public class Place{Entity}Handler : IRequestHandler<Place{Entity}Command, {Entity}Id>
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public async Task<{Entity}Id> Handle(Place{Entity}Command request, CancellationToken ct)
    {
        var userId = _httpContextAccessor.HttpContext?.User.FindFirstValue("sub")
            ?? throw new UnauthorizedAccessException();

        // Use userId for ownership, audit trail, etc.
    }
}
```

**Alternative:** Create an `ICurrentUserAccessor` abstraction to avoid coupling handlers to `HttpContext`.

## Secure Coding Practices

### SQL Injection Prevention

```csharp
// SAFE: EF Core LINQ queries are always parameterized
var entity = await _dbContext.Set<{Entity}>()
    .FirstOrDefaultAsync(e => e.Id == id, ct);

// SAFE: Interpolated SQL via EF Core (parameterized automatically)
var results = await _dbContext.Set<{Entity}>()
    .FromSqlInterpolated($"SELECT * FROM entities WHERE status = {status}")
    .ToListAsync(ct);

// DANGEROUS: Raw string concatenation — NEVER do this
var results = await _dbContext.Set<{Entity}>()
    .FromSqlRaw($"SELECT * FROM entities WHERE status = '{status}'") // SQL INJECTION!
    .ToListAsync(ct);
```

**Rule:** All database queries MUST be parameterized. No raw SQL string concatenation with user input.

### Cross-Site Scripting (XSS)

For APIs returning JSON, XSS risk is minimal but still relevant:

```csharp
// ASP.NET Core JSON serialization escapes HTML by default
// No additional action needed for standard JSON API responses

// If rendering HTML (rare for this project):
// Use HtmlEncoder.Default.Encode(userInput) before inserting into HTML
```

Frontend rules:
- React auto-escapes text content in JSX — `{userInput}` is safe
- Never use `dangerouslySetInnerHTML` with user-supplied content
- Sanitize user input before rendering as HTML (use DOMPurify if absolutely needed)

### Mass Assignment Prevention

```csharp
// BAD: Binding directly to domain entity (all properties exposed)
group.MapPut("/{id:guid}", async ({Entity} entity, ...) => { });

// GOOD: Use a specific request record with only allowed fields
group.MapPut("/{id:guid}", async (Update{Entity}Request request, ...) =>
{
    // Only map explicitly allowed fields to the command
    var command = new Update{Entity}Command(request.Status, request.Reason);
});

public record Update{Entity}Request
{
    public string Status { get; init; } = string.Empty;
    public string Reason { get; init; } = string.Empty;
    // CustomerId, Id, CreatedAt etc. are NOT here — can't be mass-assigned
}
```

**Rule:** Never bind HTTP request bodies directly to domain entities. Always use dedicated request records with only the fields the client is allowed to set.

### CSRF Protection

Minimal APIs with JWT Bearer auth are not vulnerable to CSRF (cookies are not used for auth). However:

- If cookies are ever introduced for auth, add anti-forgery tokens
- File upload endpoints use `.DisableAntiforgery()` because they use `multipart/form-data`
- SameSite cookie policy should be `Strict` if cookies are used

### Denial of Service (DoS)

Mitigations already in place:
- Request body size limit: 1 MB (middleware rejects larger payloads with 413)
- Rate limiting: fixed-window per user/IP
- Pagination: max page size enforced (100)
- Query timeout: 30 seconds at database level
- Connection pool limits: bounded by Npgsql settings

Additional rules:
- Never allow unbounded collection queries (`ToListAsync()` without `Take()`)
- Set `MaxRequestBodySize` explicitly in Kestrel config for large file uploads
- Use streaming for large responses (avoid loading entire dataset into memory)

## Input Validation Layers

| Layer | What It Catches | How |
|-------|----------------|-----|
| **ASP.NET Model Binding** | Malformed JSON, missing required properties | Automatic — returns 400 |
| **Request Size Middleware** | Oversized payloads | Rejects with 413 before deserialization |
| **FluentValidation** | Business rule violations on input | Pipeline behaviour — returns 400 with ProblemDetails |
| **Domain Invariants** | Invalid state transitions | Domain exceptions — middleware returns 409 |

### Validation Rules

- Validate at the boundary (FluentValidation on commands) — reject early
- Domain entities validate their own invariants (double protection)
- Never trust client input — validate type, range, format, and business rules
- Return specific error messages per field (ProblemDetails `errors` dictionary)
- Don't reveal internal details in validation messages (no stack traces, no SQL)

## Dependency Vulnerability Management

### Automated Scanning

| Tool | When | What It Checks |
|------|------|---------------|
| Trivy | CI on every push | Container image vulnerabilities (CRITICAL/HIGH fail build) |
| `dotnet list package --vulnerable` | CI weekly | NuGet package known vulnerabilities |
| `npm audit` | CI on frontend changes | npm package vulnerabilities |
| Dependabot | Automated PRs | Dependency version updates |
| SonarCloud | CI on every PR | Code quality, security hotspots |

### Rules

- CRITICAL and HIGH vulnerabilities fail the CI build (Trivy)
- Dependabot PRs are reviewed and merged within 1 week
- Pin NuGet and npm package versions (exact versions, not ranges)
- Review new dependencies before adding (check maintainership, popularity, license)
- No packages from unknown sources — only nuget.org and npmjs.com

## Secrets Handling in Code

### What Constitutes a Secret

| Secret | Where It Belongs | Never In |
|--------|-----------------|----------|
| Database connection strings | Secrets Manager / Key Vault | Source code, appsettings.json (prod) |
| API keys for external services | Secrets Manager / Key Vault | Source code, environment variable definitions in git |
| JWT signing keys | OIDC provider (auto-rotated) | Application configuration |
| TLS certificates | Key Vault / ACM | Source code, Docker images |
| Service account credentials | Managed identity (no credentials) | Anywhere manual |

### Prevention

- `.gitignore` excludes `*.env`, `appsettings.*.json` with secrets
- Pre-commit hooks (`gitleaks`, `git-secrets`) scan for accidental commits
- CI scans check for patterns matching secrets (API keys, connection strings)
- Code review checklist includes "No secrets or credentials committed"

### If a Secret Is Accidentally Committed

1. Rotate the secret immediately (revoke/regenerate)
2. Remove from git history (`git filter-repo` or BFG Repo Cleaner)
3. Force-push the cleaned history
4. Audit access logs for the period the secret was exposed
5. File an incident report

## CORS Security

### Configuration

```csharp
builder.Services.AddCorsPolicy(configuration);

// Extension method
public static IServiceCollection AddCorsPolicy(this IServiceCollection services, IConfiguration configuration)
{
    var origins = configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];

    services.AddCors(options =>
    {
        options.AddDefaultPolicy(policy =>
        {
            policy.WithOrigins(origins)
                .WithMethods("GET", "POST", "PUT", "DELETE", "OPTIONS")
                .WithHeaders("Authorization", "Content-Type", "X-Correlation-Id")
                .AllowCredentials()
                .SetPreflightMaxAge(TimeSpan.FromSeconds(600));
        });
    });

    return services;
}
```

### Rules

- Never use `AllowAnyOrigin()` in production — whitelist specific origins
- `AllowCredentials()` requires specific origins (not wildcard `*`)
- Preflight max-age (600s) reduces OPTIONS requests for browsers
- Only allow headers the API actually uses (Authorization, Content-Type, X-Correlation-Id)
- CORS is enforced by browsers — it does NOT protect server-to-server calls

## Encryption

### In Transit

- All external communication over HTTPS (TLS 1.2+)
- Internal service-to-service within VPC/VNet can use HTTP (network isolation provides security)
- HSTS header enforced via `SecurityHeadersMiddleware` (max-age=31536000)

### At Rest

- PostgreSQL (RDS Aurora / Azure SQL): encryption at rest enabled (AES-256)
- Redis (ElastiCache / Azure Cache): encryption at rest enabled
- S3/Blob storage: server-side encryption (SSE-S3 or SSE-KMS)
- Container images: stored in private registries (ECR/ACR) with scanning

### Application-Level Encryption

When storing sensitive fields that must be readable (not hashed):

```csharp
// Use ASP.NET Core Data Protection for symmetric encryption
builder.Services.AddDataProtection()
    .PersistKeysToDbContext<{SolutionName}DbContext>();

// Encrypt/decrypt in infrastructure layer
public class EncryptedFieldService
{
    private readonly IDataProtector _protector;

    public EncryptedFieldService(IDataProtectionProvider provider)
    {
        _protector = provider.CreateProtector("SensitiveFields");
    }

    public string Encrypt(string plaintext) => _protector.Protect(plaintext);
    public string Decrypt(string ciphertext) => _protector.Unprotect(ciphertext);
}
```

**Rule:** Only encrypt fields that must be stored reversibly (e.g., notification addresses). For passwords, use one-way hashing (bcrypt/Argon2) — never encryption.

## Security Checklist for New Features

Before shipping:

1. [ ] All endpoints use `RequireAuthorization()` unless explicitly public
2. [ ] User identity comes from JWT `sub` claim, not from request body
3. [ ] Input validated (FluentValidation + domain invariants)
4. [ ] No raw SQL string concatenation
5. [ ] No secrets in code or config files
6. [ ] Request body size limited
7. [ ] Rate limiting applied to public-facing endpoints
8. [ ] New dependencies scanned for vulnerabilities
9. [ ] Error responses don't leak internal details (no stack traces, no SQL)
10. [ ] Audit-relevant actions logged with correlation ID and user ID
