---
inclusion: manual
---

# Caching Best Practices

This document covers caching strategy, in-memory vs. distributed cache, cache-aside pattern, invalidation, Redis configuration, and testing. For output caching on endpoints, see `05-minimal-api-endpoint-conventions.md`.

## When to Cache

### Decision Gate (from Capacity Estimation)

| Read QPS (peak) | Action |
|-----------------|--------|
| < 1,000 | No caching needed — PostgreSQL handles reads directly |
| ≥ 1,000 | Add distributed cache (Redis / ElastiCache) with cache-aside |
| ≥ 10,000 | Add separate read store (read replica or CQRS projection) |

### What to Cache

| Good Cache Candidates | Why |
|----------------------|-----|
| Entity by ID (read-heavy) | Same entity read many times between writes |
| Reference/lookup data | Rarely changes (product catalog, configuration) |
| Computed aggregations | Expensive to recalculate on every request |
| External API responses | Reduce latency and dependency on external systems |
| Session/token data | High read frequency, short TTL |

### What NOT to Cache

| Bad Cache Candidates | Why |
|---------------------|-----|
| Write-heavy data | Invalidation cost exceeds cache benefit |
| User-specific mutable data with strong consistency needs | Stale reads cause business errors |
| Large payloads (> 100 KB) | Serialization cost + memory pressure |
| Data that changes every request | TTL = 0 means no caching |
| Security-sensitive data (tokens, secrets) | Risk of leaking across users |

## Cache Types in .NET

### IMemoryCache (In-Process)

```csharp
builder.Services.AddMemoryCache();
```

| Characteristic | Value |
|---------------|-------|
| Speed | ~0.001 ms (same process memory) |
| Scope | Per-instance (not shared across pods/tasks) |
| Capacity | Limited by process memory |
| Persistence | Lost on restart |
| Best for | Hot-path reads within a single instance, small reference data |

### IDistributedCache (Redis / External Store)

```csharp
builder.Services.AddStackExchangeRedisCache(options =>
{
    options.Configuration = configuration.GetConnectionString("Redis");
    options.InstanceName = "{solution-name}:";
});
```

| Characteristic | Value |
|---------------|-------|
| Speed | ~0.5-2 ms (network hop to Redis) |
| Scope | Shared across all instances/tasks |
| Capacity | Limited by Redis memory (configurable) |
| Persistence | Survives app restarts (Redis persists) |
| Best for | Data shared across instances, session data, distributed deduplication |

### When to Use Each

| Scenario | Choice | Reason |
|----------|--------|--------|
| Single instance, small data | `IMemoryCache` | No network overhead |
| Multiple instances, shared state | `IDistributedCache` (Redis) | Consistency across pods |
| Both: hot path + shared | Two-tier (memory → Redis) | Memory for speed, Redis for consistency |
| Output caching for HTTP responses | `OutputCache` middleware | Built-in, tag-based invalidation |

## Cache-Aside Pattern (Lazy Load)

The primary caching pattern in this project:

```csharp
public class Cached{Entity}Repository : I{Entity}Repository
{
    private readonly I{Entity}Repository _inner;
    private readonly IDistributedCache _cache;
    private readonly ILogger<Cached{Entity}Repository> _logger;

    private static readonly TimeSpan DefaultTtl = TimeSpan.FromMinutes(5);

    public async Task<{Entity}?> GetByIdAsync({Entity}Id id, CancellationToken ct)
    {
        var cacheKey = CacheKey(id);

        // 1. Try cache first
        var cached = await _cache.GetStringAsync(cacheKey, ct);
        if (cached is not null)
        {
            _logger.LogDebug("Cache hit for {CacheKey}", cacheKey);
            return JsonSerializer.Deserialize<{Entity}>(cached);
        }

        // 2. Cache miss — load from database
        _logger.LogDebug("Cache miss for {CacheKey}", cacheKey);
        var entity = await _inner.GetByIdAsync(id, ct);

        // 3. Populate cache (only if found)
        if (entity is not null)
        {
            var json = JsonSerializer.Serialize(entity);
            await _cache.SetStringAsync(cacheKey, json, new DistributedCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = DefaultTtl
            }, ct);
        }

        return entity;
    }

    public async Task SaveAsync({Entity} entity, CancellationToken ct)
    {
        await _inner.SaveAsync(entity, ct);

        // Invalidate cache after write
        await _cache.RemoveAsync(CacheKey(entity.Id), ct);
    }

    private static string CacheKey({Entity}Id id) => $"{{solution-name}}:{{entities}}:{id.Value}";
}
```

### DI Registration (Decorator Pattern)

```csharp
// Register real repository
builder.Services.AddScoped<Ef{Entity}Repository>();

// Register decorator
builder.Services.AddScoped<I{Entity}Repository>(sp =>
    new Cached{Entity}Repository(
        sp.GetRequiredService<Ef{Entity}Repository>(),
        sp.GetRequiredService<IDistributedCache>(),
        sp.GetRequiredService<ILogger<Cached{Entity}Repository>>()));
```

### Rules

- Read hits the cache first — only goes to DB on miss
- Write invalidates the cache — ensures next read gets fresh data
- Never return stale data after a confirmed write (invalidate, don't update)
- Cache nulls carefully — consider using a sentinel value to distinguish "not found" from "not cached"

## Cache Invalidation Strategies

### TTL-Based (Time-to-Live)

Simplest approach — cache entries expire automatically:

```csharp
new DistributedCacheEntryOptions
{
    AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(5),  // Hard expiry
    SlidingExpiration = TimeSpan.FromMinutes(2)                   // Extends if accessed
}
```

| TTL Type | Behaviour | Use When |
|----------|-----------|----------|
| Absolute | Expires at fixed time after creation | Data has known freshness window |
| Sliding | Resets on every access | Frequently-read data that can be stale if unused |
| Both combined | Evicts on sliding timeout OR absolute max | Most common pattern |

### Event-Driven Invalidation

Invalidate cache when domain events indicate data changed:

```csharp
public sealed class {Entity}PlacedCacheInvalidator : IConsumer<{Entity}PlacedEvent>
{
    private readonly IDistributedCache _cache;

    public async Task Consume(ConsumeContext<{Entity}PlacedEvent> context)
    {
        await _cache.RemoveAsync($"{{solution-name}}:{{entities}}:{context.Message.{Entity}Id.Value}");
        await _cache.RemoveAsync($"{{solution-name}}:{{entities}}:list"); // Invalidate list cache too
    }
}
```

### Write-Through (Rare)

Update cache immediately on write (instead of invalidating):

```csharp
await _inner.SaveAsync(entity, ct);
var json = JsonSerializer.Serialize(entity);
await _cache.SetStringAsync(CacheKey(entity.Id), json, options, ct);
```

Rules:
- Prefer invalidation over write-through (simpler, avoids race conditions)
- Use write-through only when reads vastly outnumber writes AND immediate freshness is critical
- Write-through adds latency to writes (must succeed both DB + cache)

## Redis Key Design

### Key Naming Convention

```
{service-name}:{resource}:{identifier}
```

Examples:
- `myapp:leadsmicroservice:550e8400-e29b-41d4-a716-446655440000` — single entity
- `myapp:leadsmicroservice:list:page:1:size:20` — paginated list
- `myapp:leadsmicroservice:customer:abc123` — entity list by customer
- `myapp:config:rate-limits` — configuration cache
- `myapp:dedup:outbox:msg-id-123` — deduplication key

### Rules

- Always prefix with service name (avoids collisions in shared Redis)
- Use `:` as separator (Redis convention, enables `SCAN` patterns)
- Include all dimensions that make the cached value unique
- Keep keys short but readable (Redis stores key bytes for each entry)
- Use `{solution-name}:` as `InstanceName` in `AddStackExchangeRedisCache`

### Key Expiration

| Data Type | TTL | Reason |
|-----------|-----|--------|
| Entity by ID | 5 minutes | Balance freshness vs. DB load |
| List/collection | 30 seconds | Changes more frequently |
| Reference data | 1 hour | Rarely changes |
| Deduplication keys | 7 days | Match outbox retention |
| Session/auth | 30 minutes | Security boundary |

## Cache Stampede Prevention

When a popular cache key expires, many concurrent requests may hit the database simultaneously:

### Locking Pattern

```csharp
public async Task<{Entity}?> GetByIdAsync({Entity}Id id, CancellationToken ct)
{
    var cacheKey = CacheKey(id);
    var cached = await _cache.GetStringAsync(cacheKey, ct);
    if (cached is not null)
        return Deserialize(cached);

    // Acquire a lock — only one request populates the cache
    var lockKey = $"{cacheKey}:lock";
    var acquired = await _cache.SetStringAsync(lockKey, "1",
        new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(10) },
        when: When.NotExists); // Only set if not already locked

    if (!acquired)
    {
        // Another request is populating — wait briefly then retry cache
        await Task.Delay(100, ct);
        cached = await _cache.GetStringAsync(cacheKey, ct);
        return cached is not null ? Deserialize(cached) : await _inner.GetByIdAsync(id, ct);
    }

    var entity = await _inner.GetByIdAsync(id, ct);
    if (entity is not null)
        await _cache.SetStringAsync(cacheKey, Serialize(entity), DefaultOptions, ct);

    await _cache.RemoveAsync(lockKey, ct);
    return entity;
}
```

### Simpler Alternative: Jittered TTL

Add random jitter to prevent synchronized expiration:

```csharp
var ttl = DefaultTtl + TimeSpan.FromSeconds(Random.Shared.Next(0, 30));
```

Rules:
- For most cases, jittered TTL is sufficient
- Use locking only for extremely hot keys (thousands of concurrent readers)
- Lock TTL must be shorter than expected DB query time + buffer
- Always have a fallback path if the lock is held (retry or direct DB query)

## Serialization

### JSON (Default)

```csharp
var json = JsonSerializer.Serialize(entity, new JsonSerializerOptions
{
    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
});
```

### Rules

- Use `System.Text.Json` (not Newtonsoft) — consistent with the rest of the project
- Cache domain entities as serialized JSON — same serializer used for outbox events
- For performance-critical paths, consider `MessagePack` (binary, faster serialization)
- Never store `IQueryable` or EF-tracked entities in cache — serialize detached data only
- Include all navigation properties needed by consumers (cache the complete read model)

## Redis Connection Configuration

### Options Pattern

```csharp
public sealed class RedisOptions
{
    public const string SectionName = "Redis";

    public string Configuration { get; init; } = "localhost:6379";
    public string InstanceName { get; init; } = "{solution-name}:";
    public int ConnectTimeout { get; init; } = 5000;
    public int SyncTimeout { get; init; } = 3000;
    public bool AbortOnConnectFail { get; init; } = false;
}
```

### Registration

```csharp
builder.Services.AddStackExchangeRedisCache(options =>
{
    var redisOptions = configuration.GetSection(RedisOptions.SectionName).Get<RedisOptions>()!;
    options.Configuration = redisOptions.Configuration;
    options.InstanceName = redisOptions.InstanceName;
    options.ConfigurationOptions = new ConfigurationOptions
    {
        ConnectTimeout = redisOptions.ConnectTimeout,
        SyncTimeout = redisOptions.SyncTimeout,
        AbortOnConnectFail = redisOptions.AbortOnConnectFail // false = resilient to Redis outages
    };
});
```

### Resilience Rules

- Set `AbortOnConnectFail = false` — app continues if Redis is temporarily unavailable
- Cache operations should be non-fatal — if Redis fails, fall through to database
- Wrap cache calls in try/catch for production resilience:

```csharp
try
{
    cached = await _cache.GetStringAsync(cacheKey, ct);
}
catch (RedisConnectionException ex)
{
    _logger.LogWarning(ex, "Redis unavailable — falling through to database");
    cached = null; // Proceed without cache
}
```

- Health check includes Redis readiness (`AddRedis()` health check)
- Monitor Redis memory usage and eviction rate in production

## Testing Cached Code

### Unit Testing the Decorator

```csharp
[Fact]
public async Task GetByIdAsync_CacheHit_DoesNotCallInnerRepository()
{
    var entity = {Entity}Faker.CreateValid();
    var json = JsonSerializer.Serialize(entity);
    _cacheMock.Setup(c => c.GetStringAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
        .ReturnsAsync(json);

    var result = await _cachedRepo.GetByIdAsync(entity.Id, CancellationToken.None);

    _innerRepoMock.Verify(r => r.GetByIdAsync(It.IsAny<{Entity}Id>(), It.IsAny<CancellationToken>()), Times.Never());
    Assert.NotNull(result);
}

[Fact]
public async Task GetByIdAsync_CacheMiss_CallsInnerRepositoryAndPopulatesCache()
{
    _cacheMock.Setup(c => c.GetStringAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
        .ReturnsAsync((string?)null);
    _innerRepoMock.Setup(r => r.GetByIdAsync(It.IsAny<{Entity}Id>(), It.IsAny<CancellationToken>()))
        .ReturnsAsync({Entity}Faker.CreateValid());

    await _cachedRepo.GetByIdAsync({Entity}Id.New(), CancellationToken.None);

    _cacheMock.Verify(c => c.SetStringAsync(It.IsAny<string>(), It.IsAny<string>(),
        It.IsAny<DistributedCacheEntryOptions>(), It.IsAny<CancellationToken>()), Times.Once());
}
```

### Integration Testing with Real Redis

For integration tests, use Testcontainers Redis:

```csharp
private readonly RedisContainer _redis = new RedisBuilder().Build();

[Fact]
public async Task CacheRoundTrip_StoresAndRetrievesEntity()
{
    await _redis.StartAsync();
    var cache = new RedisCache(new RedisCacheOptions { Configuration = _redis.GetConnectionString() });
    // Test full round-trip...
}
```

## Anti-Patterns

| Anti-Pattern | Problem | Fix |
|-------------|---------|-----|
| Cache everything by default | Memory waste, stale data risk | Cache only what's read-heavy and tolerance-appropriate |
| No TTL (infinite cache) | Memory leak, permanently stale data | Always set TTL |
| Cache invalidation on every write | Negates caching benefit for write-heavy data | Don't cache write-heavy data |
| Caching EF-tracked entities | Serialization issues, change tracker corruption | Serialize detached/projected data |
| Same TTL for all keys | Suboptimal freshness vs. hit rate | Tune TTL per data type |
| Ignoring cache failures | Silent data staleness | Log warnings, fall through to DB |
| Cache key without service prefix | Key collisions in shared Redis | Always prefix with service name |
