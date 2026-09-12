---
inclusion: fileMatch
fileMatchPattern: "**/*Options.cs,**/*Settings.cs,**/appsettings*.json,**/Program.cs"
---

# Application Configuration & Options Pattern

This document covers the .NET configuration system, the Options pattern, validation, environment-specific overrides, and secrets management for this project.

## Configuration Sources (Priority Order)

Configuration is loaded in this order (last wins):

```
1. appsettings.json                     ← base defaults
2. appsettings.{Environment}.json       ← environment overrides
3. Environment variables                ← container/cloud overrides
4. User Secrets (Development only)      ← local dev secrets
5. Command-line arguments               ← ad-hoc overrides
```

### Environment Variable Naming

.NET maps environment variables to configuration keys using `__` as section separator:

| Configuration Key | Environment Variable |
|-------------------|---------------------|
| `ConnectionStrings:{SolutionName}Db` | `ConnectionStrings__{SolutionName}Db` |
| `RabbitMq:Host` | `RabbitMq__Host` |
| `RateLimit:PermitLimit` | `RateLimit__PermitLimit` |
| `Jwt:Authority` | `Jwt__Authority` |
| `Cors:AllowedOrigins:0` | `Cors__AllowedOrigins__0` |

Rules:
- Use `__` (double underscore) for nested keys in env vars
- Array elements use numeric index: `AllowedOrigins__0`, `AllowedOrigins__1`
- Environment variables override JSON files — use for per-deployment configuration
- In Docker Compose and ECS/Container Apps, set env vars in container definition

## The Options Pattern

### IOptions<T> vs. IOptionsSnapshot<T> vs. IOptionsMonitor<T>

| Interface | Lifetime | Reloads | Use When |
|-----------|----------|---------|----------|
| `IOptions<T>` | Singleton | Never (reads once at startup) | Static config that never changes at runtime |
| `IOptionsSnapshot<T>` | Scoped | Per request | Config that may change between requests (rare) |
| `IOptionsMonitor<T>` | Singleton | On file change | Background services that need hot-reload |

### Rules

- **Default choice**: `IOptions<T>` — most configuration is static per deployment
- Use `IOptionsMonitor<T>` only in background services (`OutboxProcessor`, `OutboxRetentionService`) where restarting the process for config changes is undesirable
- Never inject `IConfiguration` directly into handlers or domain services — use typed options
- `IOptionsSnapshot<T>` is rarely needed — prefer `IOptions<T>` unless you have a proven hot-reload requirement

## Defining Options Classes

### Structure

Place options classes alongside the extension methods that consume them, in the Infrastructure or Api layer:

```csharp
namespace {SolutionName}.Infrastructure.Messaging;

public sealed class RabbitMqOptions
{
    public const string SectionName = "RabbitMq";

    public string Host { get; init; } = string.Empty;
    public string Username { get; init; } = "guest";
    public string Password { get; init; } = "guest";
    public int ConsumerRetryCount { get; init; } = 3;
    public int StartupRetryAttempts { get; init; } = 5;
}
```

### Rules

- `sealed class` with `init` properties (immutable after binding)
- Include a `const string SectionName` matching the JSON section name
- Provide sensible defaults for non-required properties
- Use `string.Empty` as default for required strings (validation catches empty)
- Naming: `{Feature}Options` (e.g., `RateLimitOptions`, `OutboxOptions`, `JwtOptions`)

### File Placement

| Options Class | Layer | Reason |
|---------------|-------|--------|
| `RabbitMqOptions` | Infrastructure | Messaging configuration |
| `OutboxOptions` | Infrastructure | Background service configuration |
| `RateLimitOptions` | Api | Middleware configuration |
| `JwtOptions` | Api | Authentication configuration |
| `CorsOptions` | Api | CORS policy configuration |

## Registration & Validation

### Basic Registration

```csharp
// In Program.cs or extension method
builder.Services.Configure<RabbitMqOptions>(
    builder.Configuration.GetSection(RabbitMqOptions.SectionName));
```

### Registration with Validation (Preferred)

```csharp
builder.Services.AddOptions<RateLimitOptions>()
    .Bind(builder.Configuration.GetSection(RateLimitOptions.SectionName))
    .ValidateDataAnnotations()
    .ValidateOnStart(); // Fails fast at startup if config is invalid
```

### Data Annotations for Validation

```csharp
using System.ComponentModel.DataAnnotations;

public sealed class RateLimitOptions
{
    public const string SectionName = "RateLimit";

    [Range(1, 10000)]
    public int PermitLimit { get; init; } = 100;

    [Range(1, 3600)]
    public int WindowSeconds { get; init; } = 60;
}
```

### Custom Validation (Complex Rules)

```csharp
builder.Services.AddOptions<OutboxOptions>()
    .Bind(builder.Configuration.GetSection(OutboxOptions.SectionName))
    .Validate(options =>
    {
        if (options.BatchSize > 0 && options.MaxRetries > 0)
            return true;
        return false;
    }, "BatchSize and MaxRetries must be positive.")
    .ValidateOnStart();
```

### Rules

- Always use `ValidateOnStart()` for critical configuration — fail fast, don't discover misconfiguration at runtime
- Use `ValidateDataAnnotations()` for simple range/required checks
- Use `Validate(...)` for cross-property rules
- If validation fails at startup, the application crashes with a clear error message — this is intentional

## Consuming Options

### In Handlers and Services

```csharp
public class OutboxProcessor : BackgroundService
{
    private readonly IOptionsMonitor<OutboxOptions> _options;

    public OutboxProcessor(IOptionsMonitor<OutboxOptions> options)
    {
        _options = options; // Hot-reloadable for background services
    }

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            var batchSize = _options.CurrentValue.BatchSize;
            // Process batch...
            await Task.Delay(TimeSpan.FromSeconds(_options.CurrentValue.PollingIntervalSeconds), ct);
        }
    }
}
```

### In Extension Methods

```csharp
public static class RateLimitServiceCollectionExtensions
{
    public static IServiceCollection Add{SolutionName}RateLimiter(
        this IServiceCollection services, IConfiguration configuration)
    {
        var options = configuration
            .GetSection(RateLimitOptions.SectionName)
            .Get<RateLimitOptions>() ?? new RateLimitOptions();

        services.AddRateLimiter(limiter =>
        {
            limiter.AddFixedWindowLimiter("{solution-name}-api", window =>
            {
                window.PermitLimit = options.PermitLimit;
                window.Window = TimeSpan.FromSeconds(options.WindowSeconds);
            });
        });

        return services;
    }
}
```

### Anti-Patterns

```csharp
// BAD: Injecting IConfiguration directly
public class Place{Entity}Handler
{
    private readonly IConfiguration _config; // Too broad, no type safety
    public Place{Entity}Handler(IConfiguration config) { _config = config; }

    public async Task Handle(...)
    {
        var limit = _config.GetValue<int>("RateLimit:PermitLimit"); // Magic string!
    }
}

// GOOD: Inject typed options
public class Place{Entity}Handler
{
    private readonly IOptions<RateLimitOptions> _options;
    public Place{Entity}Handler(IOptions<RateLimitOptions> options) { _options = options; }

    public async Task Handle(...)
    {
        var limit = _options.Value.PermitLimit; // Typed, validated, discoverable
    }
}
```

## Configuration Sections in This Project

### appsettings.json Structure

```json
{
  "ConnectionStrings": {
    "{SolutionName}Db": "Host=localhost;Port=5432;Database={solution_name};Username=postgres;Password=postgres"
  },
  "RabbitMq": {
    "Host": "localhost",
    "Username": "guest",
    "Password": "guest",
    "ConsumerRetryCount": 3,
    "StartupRetryAttempts": 5
  },
  "Outbox": {
    "BatchSize": 20,
    "MaxRetries": 5,
    "PollingIntervalSeconds": 5,
    "Retention": {
      "IntervalMinutes": 60,
      "RetentionDays": 7,
      "BatchSize": 500
    }
  },
  "RateLimit": {
    "PermitLimit": 100,
    "WindowSeconds": 60
  },
  "Jwt": {
    "Authority": "https://your-oidc-provider.com",
    "Audience": "your-api-audience"
  },
  "Cors": {
    "AllowedOrigins": ["http://localhost:3000"]
  },
  "Serilog": {
    "MinimumLevel": {
      "Default": "Information"
    }
  }
}
```

### Environment Overrides (appsettings.Development.json)

```json
{
  "Serilog": {
    "MinimumLevel": {
      "Default": "Debug",
      "Override": {
        "Microsoft.EntityFrameworkCore.Database.Command": "Information"
      }
    }
  }
}
```

Rules:
- `appsettings.json` contains all keys with sensible defaults
- `appsettings.Development.json` overrides only what differs for local dev (log levels, shorter timeouts)
- Production values come from environment variables — never commit prod secrets to JSON files
- Keep `appsettings.json` as a documentation reference for all available configuration keys

## Secrets Management

### What Goes Where

| Type | Store In | Example |
|------|----------|---------|
| Structural config (non-sensitive) | `appsettings.json` + env vars | `RateLimit:PermitLimit`, `Outbox:BatchSize` |
| Connection strings (dev) | User Secrets (local) or Docker env vars | `ConnectionStrings__{SolutionName}Db` |
| Connection strings (prod) | Secrets Manager / Key Vault | Injected as env vars via ECS task def or Container App secrets |
| API keys and tokens | Secrets Manager / Key Vault | Never in source control |
| Certificates | Key Vault / Secrets Manager | Managed identity access |

### User Secrets (Local Development)

```bash
dotnet user-secrets init --project src/{SolutionName}.Api
dotnet user-secrets set "ConnectionStrings:{SolutionName}Db" "Host=localhost;..."
dotnet user-secrets set "Jwt:Authority" "https://dev-oidc.example.com"
```

Rules:
- Use User Secrets for local development secrets — never commit them
- User Secrets are stored outside the repo (`~/.microsoft/usersecrets/`)
- In Docker Compose, secrets are passed as environment variables in `docker-compose.yml`
- In CI, secrets come from GitHub Secrets → environment variables
- In production, secrets come from Secrets Manager / Key Vault → container env vars

## Feature Flags via Configuration

For simple on/off flags, use `IConfiguration` booleans:

```json
{
  "Features": {
    "EnableNotifications": true,
    "EnableCaching": false
  }
}
```

```csharp
public sealed class FeatureOptions
{
    public const string SectionName = "Features";

    public bool EnableNotifications { get; init; } = true;
    public bool EnableCaching { get; init; } = false;
}
```

Rules:
- Use simple boolean flags until you need percentage rollouts or user targeting
- Remove flags within 2 sprints of full rollout (YAGNI — don't accumulate dead flags)
- Guard feature-flagged code at the composition root (DI registration) when possible, not in business logic
- For advanced feature management (A/B testing, gradual rollout), introduce a dedicated feature flag service

## Adding New Configuration

Checklist when adding a new configuration section:

1. Create `{Feature}Options` class with `SectionName`, defaults, and validation attributes
2. Register with `AddOptions<T>().Bind(...).ValidateDataAnnotations().ValidateOnStart()`
3. Add the section to `appsettings.json` with documented defaults
4. Add environment variable documentation to `10-docker-cicd-awareness.md`
5. If the value is sensitive, document that it must come from Secrets Manager / Key Vault
6. Inject `IOptions<T>` (or `IOptionsMonitor<T>` for background services) — never raw `IConfiguration`
