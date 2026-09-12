---
inclusion: manual
---

# Scaling System Design: Zero to Millions

This document provides a progressive scaling playbook — from single-instance deployment to multi-region, millions-of-users architecture. Each stage introduces complexity only when metrics demand it (YAGNI applies to infrastructure too).

## Progressive Scaling Stages

```
Stage 1: Single Instance (0 – 10K DAU)
    ↓ Read QPS > 100 or p99 > 200ms
Stage 2: Vertical Scaling + Load Balancer (10K – 100K DAU)
    ↓ Read QPS > 1,000 or DB CPU > 70%
Stage 3: Caching + Read Replicas (100K – 500K DAU)
    ↓ Write QPS > 500 or single DB bottleneck
Stage 4: CQRS Read Store + Queue-Based Writes (500K – 2M DAU)
    ↓ Data > 500GB or cross-region latency requirements
Stage 5: Sharding + Geographic Distribution (2M+ DAU)
```

**Rule:** Never jump stages. Each stage has measurable trigger metrics. Premature scaling is wasted engineering.

---

## Stage 1: Single Instance (0 – 10K DAU)

### Architecture

```
Browser → CDN (static) → API Gateway → Single Fargate Task → Single PostgreSQL Instance
```

### What You Have

| Component | Spec | Capacity |
|-----------|------|----------|
| Compute | 1× Fargate (0.5 vCPU / 1 GB) | ~2,000–4,000 req/s |
| Database | 1× RDS db.t3.micro | ~5,000 reads/s, ~1,000 writes/s |
| Cache | None | — |
| Message broker | RabbitMQ single node | ~3,000 msgs/s |
| CDN | CloudFront / Front Door | Static assets only |

### Design Principles

- Monolithic deployment is fine — all services in one container if traffic is low
- Focus on correctness, testing, and clean architecture — not scale
- Horizontal scaling readiness: stateless services, no local file storage, no in-memory session state
- Database migrations are simple (single instance, no replication lag concerns)

### When to Move to Stage 2

| Metric | Threshold | Action |
|--------|-----------|--------|
| p99 latency | > 200ms sustained | Investigate slow queries, then scale vertically |
| CPU utilization | > 70% sustained | Scale up instance size |
| Request rate | > 100 req/s sustained | Add load balancer + second instance |
| Database connections | > 80% of pool | Scale DB instance or add pooler |

---

## Stage 2: Vertical Scaling + Load Balancer (10K – 100K DAU)

### Architecture

```
Browser → CDN → API Gateway → ALB → [Fargate Task 1, Task 2, Task 3] → PostgreSQL (scaled up)
```

### Changes from Stage 1

| Component | Change | Why |
|-----------|--------|-----|
| Compute | 2–4× Fargate tasks behind ALB | Horizontal scaling for request handling |
| Database | Upgrade to db.t3.medium or db.r6g.large | More CPU/memory for queries |
| Auto-scaling | CPU-based (target 60%) | React to load spikes |
| Health checks | Liveness + readiness probes | ALB routes only to healthy tasks |

### Design Principles

- Services MUST be stateless — no local file storage, no in-memory state shared across requests
- Session affinity (sticky sessions) is NOT used — any task can handle any request
- Database is still single-writer (primary instance)
- Connection pooling becomes important (PgBouncer or Npgsql pool settings)

### Auto-Scaling Configuration

```yaml
# ECS Service auto-scaling
ScalingPolicy:
  TargetTrackingScaling:
    TargetValue: 60
    PredefinedMetricSpecification:
      PredefinedMetricType: ECSServiceAverageCPUUtilization
  ScaleOutCooldown: 60
  ScaleInCooldown: 300
  MinCapacity: 2
  MaxCapacity: 10
```

Rules:
- Scale out aggressively (60s cooldown) — handle spikes fast
- Scale in conservatively (300s cooldown) — avoid thrashing
- Minimum 2 tasks for high availability (multi-AZ)
- Maximum based on database connection capacity

### When to Move to Stage 3

| Metric | Threshold | Action |
|--------|-----------|--------|
| Read QPS | > 1,000 sustained | Add caching layer (Redis) |
| DB CPU | > 70% sustained (reads dominate) | Add read replica |
| p99 latency | > 100ms with indexes optimized | Cache hot-path reads |
| Database size | > 100 GB | Plan partitioning strategy |

---

## Stage 3: Caching + Read Replicas (100K – 500K DAU)

### Architecture

```
Browser → CDN → API Gateway → ALB → [Fargate Tasks] → Redis Cache
                                                      → PostgreSQL Primary (writes)
                                                      → PostgreSQL Replica (reads)
```

### Changes from Stage 2

| Component | Change | Why |
|-----------|--------|-----|
| Cache | ElastiCache Redis (cache-aside) | Offload read traffic from DB |
| Database | Add read replica | Separate read/write workloads |
| Query routing | Writes → primary, reads → replica | Utilize both instances |
| CDN | Cache API responses for public data | Reduce origin load |

### Caching Strategy

```
Cache Hit Rate Target: > 80%

Hot data (by-ID lookups): Cache-aside, TTL 5 min
List queries: Output cache, TTL 30 sec
Reference data: Cache-aside, TTL 1 hour
Write path: Invalidate cache → write to primary
```

See `25-caching-best-practices.md` for implementation details.

### Read Replica Routing

```csharp
// Read-only queries use the replica connection
builder.Services.AddDbContext<{SolutionName}ReadDbContext>(options =>
    options.UseNpgsql(configuration.GetConnectionString("{SolutionName}DbReadReplica"))
           .UseQueryTrackingBehavior(QueryTrackingBehavior.NoTracking));

// Write operations use the primary
builder.Services.AddDbContext<{SolutionName}DbContext>(options =>
    options.UseNpgsql(configuration.GetConnectionString("{SolutionName}Db")));
```

### Replication Lag Awareness

- Read replicas have ~1-5ms lag (Aurora) or ~seconds (standard RDS)
- After a write, reading from replica may return stale data
- Options: read-after-write from primary for critical paths, or accept eventual consistency for lists

### When to Move to Stage 4

| Metric | Threshold | Action |
|--------|-----------|--------|
| Write QPS | > 500 sustained | Queue-based write path (async) |
| Read QPS | > 10,000 (even with cache) | Dedicated read store (CQRS projection) |
| Data growth | > 500 GB / year | Partition strategy required |
| Event consumers | Falling behind (growing lag) | Scale consumers independently |

---

## Stage 4: CQRS Read Store + Queue-Based Writes (500K – 2M DAU)

### Architecture

```
Browser → CDN → API Gateway → ALB → [API Tasks]
                                       ├── Writes → Queue → [Worker Tasks] → PostgreSQL Primary
                                       └── Reads → Redis / Read-Optimized Store
                                       
Events → [Consumer Tasks] → Materialized Views / Read Store
```

### Changes from Stage 3

| Component | Change | Why |
|-----------|--------|-----|
| Write path | Async via queue (SNS/SQS) → worker processes | Absorb write spikes, decouple from DB |
| Read store | Separate materialized view (denormalized for queries) | Purpose-built for read patterns |
| Consumer scaling | Independent auto-scaling (queue depth metric) | Handle event bursts without affecting API |
| Database | Partitioned tables for high-growth entities | Keep query performance with large datasets |

### Queue-Based Load Leveling

```
API Task receives write request
    → Validates (synchronous)
    → Enqueues to SQS (fast, ~5ms)
    → Returns 202 Accepted to client

Worker Task (separate auto-scaling group)
    → Dequeues message
    → Processes write (domain logic + persistence)
    → Publishes domain event to outbox
```

Benefits:
- API latency is decoupled from database write latency
- Burst traffic is absorbed by the queue (SQS scales infinitely)
- Workers scale independently based on queue depth
- Client gets fast acknowledgment (202), polls for completion

### CQRS Materialized Views

```
Domain Event Published → Consumer builds read model

Read model is denormalized for specific query patterns:
- List views: pre-joined, pre-sorted, paginated
- Detail views: pre-aggregated with computed fields
- Search: indexed in Elasticsearch/OpenSearch (if needed)
```

### When to Move to Stage 5

| Metric | Threshold | Action |
|--------|-----------|--------|
| Single-region latency | > 200ms for distant users | Geographic distribution (multi-region) |
| Data volume | > 5 TB | Database sharding required |
| Write QPS | > 5,000 sustained | Partitioned writes across shards |
| Availability requirement | > 99.99% | Multi-region active-active |

---

## Stage 5: Sharding + Geographic Distribution (2M+ DAU)

### Architecture

```
Users (US) → CDN Edge → US API Gateway → US Cluster → US Database Shard
Users (EU) → CDN Edge → EU API Gateway → EU Cluster → EU Database Shard

Cross-region replication for global reads
Regional writes for data locality
```

### Database Sharding

| Strategy | When | How |
|----------|------|-----|
| Range-based | Time-series data (leadsmicroservice by date) | Partition by month/year |
| Hash-based | Uniform distribution needed | Hash(customerId) % shard_count |
| Geographic | Data sovereignty requirements | Region-based shards |
| Tenant-based | Multi-tenant SaaS | One shard per large tenant, shared for small |

### Rules for Sharding

- Avoid sharding as long as possible — it adds extreme complexity
- Choose shard key carefully — it cannot be changed easily later
- Cross-shard queries are expensive — design read patterns around shard boundaries
- Each shard is a full PostgreSQL instance with its own replica set
- Application routing layer determines which shard handles each request

### Geographic Distribution

| Pattern | Use Case | Trade-off |
|---------|----------|-----------|
| Active-passive | Disaster recovery | Higher RTO, simpler |
| Active-active reads | Global read performance | Replication lag for writes |
| Active-active writes | Global write performance | Conflict resolution needed |
| Follow-the-sun | Regional data sovereignty | Complex routing, data partitioning |

### Cost Considerations at Scale

| Component | Cost Driver | Optimization |
|-----------|------------|-------------|
| Compute | Number of tasks × hours | Right-size instances, spot/preemptible for workers |
| Database | Instance size + storage + I/O | Read replicas cheaper than scaling primary |
| Cache | Memory size × nodes | TTL tuning, eviction policies |
| Messaging | Message count + data transfer | Batch messages, compress payloads |
| CDN | Requests + data transfer | Higher cache hit ratio = lower origin costs |
| Data transfer | Cross-AZ and cross-region | Keep traffic within AZ when possible |

---

## Horizontal Scaling Readiness Checklist

Ensure these properties from day one (even at Stage 1):

| Property | How It's Achieved | Verified By |
|----------|-------------------|-------------|
| Stateless services | No local file storage, no in-memory session | Architecture tests |
| External state | Database, Redis, S3 for all persistent state | Code review |
| No sticky sessions | Any instance handles any request | Load test with multiple instances |
| Idempotent consumers | Duplicate event delivery is safe | Property-based tests |
| Connection pooling | Npgsql pool, HttpClient factory | Configuration review |
| Health checks | Liveness + readiness for orchestrator | Integration tests |
| Graceful shutdown | `CancellationToken` respected, drain connections | Manual verification |
| Configuration via env vars | No local config files for secrets/endpoints | Docker Compose + CI |

---

## Auto-Scaling Patterns

### Request-Based (API Tasks)

```yaml
Metric: ALBRequestCountPerTarget
Target: 1000 requests per target per minute
MinCapacity: 2
MaxCapacity: 20
```

### CPU-Based (General Purpose)

```yaml
Metric: ECSServiceAverageCPUUtilization
Target: 60%
MinCapacity: 2
MaxCapacity: 10
```

### Queue-Depth Based (Worker Tasks)

```yaml
Metric: ApproximateNumberOfMessagesVisible (SQS)
Target: 0 (scale to drain queue)
MinCapacity: 1
MaxCapacity: 50
ScaleOutCooldown: 30
```

### Scheduled (Predictable Patterns)

```yaml
# Scale up before known peak hours
Schedule: "cron(0 8 * * MON-FRI)"  → MinCapacity: 5
Schedule: "cron(0 20 * * *)"        → MinCapacity: 2
```

---

## Performance Budgets

### Per-Request Latency Budget

```
Total p99 target: 200ms

Allocation:
  Network (client → CDN → API Gateway): 10ms
  TLS handshake (amortized): 5ms
  Auth token validation: 5ms
  Middleware pipeline: 10ms
  Handler + domain logic: 20ms
  Cache lookup (Redis): 2ms
  Database query (if cache miss): 10ms
  Serialization + response: 5ms
  Buffer for variance: 133ms
```

### Throughput Budget

```
Per Fargate Task (0.5 vCPU / 1 GB):
  Theoretical max: ~4,000 req/s
  Realistic with I/O: ~1,000–2,000 req/s
  Target utilization: 60% → ~600–1,200 req/s per task

Scale formula:
  Required tasks = Peak QPS ÷ 1,000 (conservative)
  
Example at 500K DAU:
  Peak QPS = 60 req/s → 1 task sufficient
  Peak QPS = 600 req/s → 1 task sufficient
  Peak QPS = 6,000 req/s → 6 tasks needed
```

---

## Decision Matrix: When to Add What

| Problem | Signal | Solution | Stage |
|---------|--------|----------|-------|
| Slow reads | p99 > 100ms, DB CPU high | Add Redis cache | 3 |
| DB read bottleneck | Replica lag acceptable | Add read replica | 3 |
| Write spikes overwhelm DB | Queue depth growing, timeouts | Queue-based writes | 4 |
| Complex read queries slow | Joins across many tables | Materialized read model | 4 |
| Global user latency | > 200ms from distant regions | Geographic distribution | 5 |
| Data too large for one instance | > 5 TB, queries degrading | Sharding | 5 |
| Unpredictable traffic spikes | Auto-scaling too slow | Queue-based load leveling | 4 |
| Static content load on origin | High CDN miss rate | Tune CDN caching rules | 2 |
| Connection pool exhaustion | Max connections reached | PgBouncer or scale DB | 2 |
| Single point of failure | One AZ outage kills service | Multi-AZ deployment | 2 |

---

## Consistent Hashing & Data Partitioning

### The Problem

When distributing data across N nodes (cache servers, DB shards), naive modulo hashing (`hash(key) % N`) breaks when nodes are added or removed — almost all keys remap, causing massive cache misses or data migrations.

### How Consistent Hashing Works

```
1. Map both nodes and keys onto a circular hash ring (0 → 2^32)
2. Each key is assigned to the next node clockwise on the ring
3. When a node is added/removed, only keys between it and its predecessor remap
   → ~1/N keys move instead of ~all keys
```

```
         Node A
        /      \
   Key1 ●       ● Key4
      /           \
 Node D             Node B
      \           /
   Key3 ●       ● Key2
        \      /
         Node C

Adding Node E between A and B:
  Only keys between A and E remap → minimal disruption
```

### Virtual Nodes (Vnodes)

Physical nodes may receive uneven load if they're poorly distributed on the ring. Virtual nodes fix this:

```
Physical Node A → vnode_A_1, vnode_A_2, ... vnode_A_150
Physical Node B → vnode_B_1, vnode_B_2, ... vnode_B_150

More vnodes per physical node → more even distribution
Typical: 100-200 vnodes per physical node
```

### When Consistent Hashing Applies in This Architecture

| Component | How It's Used | Who Manages It |
|-----------|---------------|----------------|
| Redis Cluster | Slot-based distribution (16,384 slots) | ElastiCache / Azure Cache (automatic) |
| Database Sharding | Route queries to correct shard | Application routing layer |
| CDN Edge Caching | Route requests to nearest edge node | CloudFront / Front Door (automatic) |
| Message Partitioning | Route messages to correct partition | SQS FIFO partition key / Service Bus sessions |

### Application-Level Consistent Hashing (When Needed)

If you implement custom sharding in the application layer:

```csharp
public class ConsistentHashRing<TNode>
{
    private readonly SortedDictionary<uint, TNode> _ring = new();
    private readonly int _virtualNodesPerNode;

    public ConsistentHashRing(int virtualNodesPerNode = 150)
    {
        _virtualNodesPerNode = virtualNodesPerNode;
    }

    public void AddNode(TNode node)
    {
        for (int i = 0; i < _virtualNodesPerNode; i++)
        {
            var hash = ComputeHash($"{node}:vnode:{i}");
            _ring[hash] = node;
        }
    }

    public void RemoveNode(TNode node)
    {
        for (int i = 0; i < _virtualNodesPerNode; i++)
        {
            var hash = ComputeHash($"{node}:vnode:{i}");
            _ring.Remove(hash);
        }
    }

    public TNode GetNode(string key)
    {
        var hash = ComputeHash(key);
        // Find the first node clockwise on the ring
        foreach (var entry in _ring)
        {
            if (entry.Key >= hash)
                return entry.Value;
        }
        return _ring.First().Value; // Wrap around
    }

    private static uint ComputeHash(string input)
    {
        var bytes = System.Security.Cryptography.MD5.HashData(Encoding.UTF8.GetBytes(input));
        return BitConverter.ToUInt32(bytes, 0);
    }
}
```

### Partitioning Strategy Decision Guide

| Strategy | Best For | Drawback |
|----------|----------|----------|
| **Hash-based (consistent)** | Uniform distribution, key-value lookups | Range queries require scatter-gather |
| **Range-based** | Time-series, ordered data, range scans | Hot partitions if recent data is most active |
| **Geographic** | Data locality, sovereignty compliance | Cross-region queries are expensive |
| **Directory-based** | Flexible, custom routing logic | Lookup service becomes single point of failure |

### Rules

- Let managed services handle partitioning when possible (Redis Cluster, Aurora, SQS FIFO)
- Only implement application-level consistent hashing when managed services don't fit
- Use consistent hashing for cache cluster routing if running self-managed Redis
- Use range-based partitioning for time-series data (logs, events, metrics)
- Hash-based for uniform distribution where key-range queries aren't needed
- Document shard key choice in an ADR — it's nearly irreversible once data grows

---

## Anti-Patterns in Scaling

| Anti-Pattern | Why It Fails | Do Instead |
|-------------|-------------|-----------|
| Premature sharding | Extreme complexity for no benefit | Scale vertically first, then cache, then replicas |
| Scaling compute without profiling | Throwing money at wrong bottleneck | Profile first (is it CPU, I/O, memory, or network?) |
| Shared database across services | Single bottleneck, coupling | Database per service |
| Synchronous cross-service calls under load | Cascading failures | Async events, circuit breakers |
| No caching strategy | Every request hits DB | Cache-aside for read-heavy paths |
| Infinite retry without backoff | Amplifies failures (retry storm) | Exponential backoff + circuit breaker |
| Scaling without observability | Can't identify bottlenecks | Metrics + tracing first, then scale |
| Same auto-scaling for all services | Different services have different profiles | Per-service scaling policies |
