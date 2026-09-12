---
inclusion: manual
---

# Architecture Fundamentals (ISAQB-Aligned)

This document covers quality attributes, architecture documentation, coupling/cohesion analysis, and technical debt management — aligned with the iSAQB Foundation Level curriculum. For specific patterns and decisions, see the ADRs and domain-specific steering files.

## Quality Attributes (ISO 25010)

Every architectural decision in this project is justified by one or more quality attributes. These are the measurable "-ilities" that drive trade-offs.

### Quality Model for This Project

| Quality Attribute | Priority | How the Architecture Achieves It |
|-------------------|:--------:|----------------------------------|
| **Maintainability** | High | Clean Architecture layers, SOLID, one-handler-per-operation, architecture tests |
| **Testability** | High | Domain isolation (no infra deps), DI everywhere, pure domain logic, PBT |
| **Performance** | Medium | Caching gates, pagination, async I/O, output caching, query optimization |
| **Reliability** | High | Outbox pattern (no event loss), retry + DLQ, health checks, multi-AZ |
| **Security** | High | JWT auth, input validation, rate limiting, parameterized queries, OWASP headers |
| **Portability** | Medium | Transport abstraction (MassTransit), IConfiguration, Docker containers |
| **Scalability** | Medium | Stateless services, async events, horizontal scaling readiness |
| **Interoperability** | Medium | REST/OpenAPI, standard message formats, correlation IDs |
| **Deployability** | High | Independent services, Docker, CI/CD, blue/green, no downtime migrations |

### Quality Attribute Scenarios

Use this template to make quality goals measurable:

```
Source:       [Who/what triggers the scenario]
Stimulus:     [What event occurs]
Artifact:     [Which component is affected]
Environment:  [Under what conditions]
Response:     [How the system reacts]
Measure:      [How we verify success]
```

#### Example: Performance

```
Source:       Authenticated user
Stimulus:     Places an entity via POST /api/{entities}
Artifact:     {Entity} Service API
Environment:  Normal load (< 100 req/s)
Response:     Entity is persisted, event is published
Measure:      p99 latency < 200ms, confirmed by OpenTelemetry metrics
```

#### Example: Reliability

```
Source:       Message broker
Stimulus:     RabbitMQ becomes unreachable for 60 seconds
Artifact:     Outbox Processor
Environment:  Production, 10 pending messages
Response:     Messages accumulate in outbox table, published when broker recovers
Measure:      Zero events lost, consumer receives all events after recovery
```

#### Example: Maintainability

```
Source:       New developer
Stimulus:     Adds a new aggregate with CRUD endpoints
Artifact:     All layers (Domain → Application → Infrastructure → Api)
Environment:  Normal development
Response:     Feature is implemented following existing patterns
Measure:      < 2 days for a developer unfamiliar with the codebase
```

### Rules

- Every ADR must reference which quality attributes it optimizes and which it trades off
- When quality attributes conflict, document the trade-off explicitly (e.g., "performance vs. consistency")
- Quality priorities may differ per service (Notifications prioritizes reliability; BFF prioritizes performance)

---

## Architecture Documentation

### Documentation Levels (C4 Model)

| Level | What It Shows | Where It Lives |
|-------|---------------|----------------|
| **Context** | System boundaries, external actors, integrations | `docs/bounded-contexts.md` |
| **Container** | Services, databases, message brokers, CDN | `docs/cloud-topology/` |
| **Component** | Layers within a service (Domain, Application, Infrastructure, Api) | Steering `01-clean-architecture-layer-placement.md` |
| **Code** | Classes, interfaces, relationships | Source code + architecture tests |

### What to Document

| Artifact | Purpose | Update Frequency |
|----------|---------|-----------------|
| ADRs | Capture WHY a decision was made | On every significant decision |
| Bounded context map | Show service boundaries and event flow | On new service or boundary change |
| Cloud topology | Show deployment infrastructure | On infrastructure changes |
| Capacity estimation | Show scaling projections | Before go-live and at growth milestones |
| Steering files | Show HOW to implement patterns | On convention changes |

### What NOT to Document

- Implementation details that are obvious from reading the code
- Class diagrams that mirror the source (they go stale immediately)
- Step-by-step deployment instructions (CI/CD handles this)
- Meeting notes or design discussions (use ADRs for conclusions only)

### Architecture Decision Records (ADRs)

```markdown
# ADR-NNN: Title

## Status
Proposed | Accepted | Deprecated | Superseded by ADR-XXX

## Context
What problem are we solving? What constraints exist?

## Decision
What did we decide? How is it implemented?

## Consequences
### Positive — Benefits gained
### Negative — Trade-offs accepted
```

Rules:
- Create an ADR for every decision that affects the system's structure or quality attributes
- ADRs are immutable once accepted — create a new ADR to supersede, don't edit old ones
- Reference affected quality attributes explicitly
- Keep them concise — 1-2 pages maximum

---

## Coupling & Cohesion

### Coupling (Minimize)

Coupling measures how much one module depends on another. Lower coupling = easier to change independently.

| Coupling Type | Example in This Project | Severity |
|--------------|------------------------|----------|
| **Data coupling** (good) | Handler passes `{Entity}Id` to repository | Low — only data exchanged |
| **Stamp coupling** (acceptable) | Handler passes full command to validator | Medium — shared data structure |
| **Control coupling** (avoid) | Passing a boolean flag that changes behaviour | High — caller controls callee logic |
| **Content coupling** (forbidden) | Directly accessing another class's private fields | Highest — violates encapsulation |
| **External coupling** (managed) | Two services depend on same message schema | Managed via event contracts |

### How This Project Minimizes Coupling

| Mechanism | What It Decouples |
|-----------|-------------------|
| Interface injection (DI) | Handlers from infrastructure implementations |
| Domain events + outbox | Publisher from consumer (temporal + spatial) |
| MediatR pipeline | Endpoint from handler |
| DTOs at boundaries | Internal domain model from external API contract |
| Configuration abstraction | Code from deployment environment |
| Architecture tests | Enforce allowed dependency directions |

### Cohesion (Maximize)

Cohesion measures how related the responsibilities within a module are. Higher cohesion = clearer purpose.

| Cohesion Type | Description | Goal |
|--------------|-------------|------|
| **Functional** (best) | All elements contribute to one well-defined task | Handler does one thing |
| **Sequential** | Output of one becomes input of next | Pipeline behaviours |
| **Communicational** | Elements operate on same data | Entity + its value objects |
| **Logical** (avoid) | Elements are grouped by category, not purpose | "Utilities" class |
| **Coincidental** (worst) | No meaningful relationship | "Helpers" namespace with unrelated methods |

### Rules

- Each class achieves functional cohesion (single responsibility)
- Each layer achieves communicational cohesion (all elements work on the same concern level)
- Each service achieves functional cohesion (one bounded context)
- Avoid "Utils", "Helpers", "Common" namespaces — they indicate low cohesion
- If a namespace grows beyond 10 classes, evaluate whether it contains multiple concerns

---

## Technical Debt Management

### What Is Technical Debt

Code or architecture that works today but increases the cost of future changes. It accumulates when:
- Shortcuts are taken under deadline pressure
- Requirements evolve but code doesn't adapt
- Dependencies fall behind on versions
- Tests are skipped or coverage drops
- Documentation lags behind implementation

### Debt Categories

| Category | Examples | Impact |
|----------|---------|--------|
| **Code debt** | Magic numbers, long methods, dead code, missing tests | Slows feature development |
| **Architecture debt** | Layer violations, circular dependencies, leaky abstractions | Makes structural changes expensive |
| **Infrastructure debt** | Outdated base images, unpatched dependencies, manual deployment steps | Security risk, operational fragility |
| **Documentation debt** | Stale ADRs, missing steering guidance, undocumented conventions | Slows onboarding, causes inconsistency |
| **Test debt** | Low coverage, flaky tests, missing property tests | Reduces confidence in changes |

### Identifying Debt

| Signal | Likely Debt | Action |
|--------|------------|--------|
| Architecture test failures | Layer violations creeping in | Fix immediately (blocking) |
| SonarCloud code smells increasing | Code debt accumulating | Allocate in next sprint |
| Dependabot PRs piling up | Infrastructure debt | Weekly triage and merge |
| New developer takes > 3 days for standard feature | Documentation or architecture debt | Improve steering files |
| Test flakiness > 1% | Test debt | Fix or quarantine flaky tests |
| Coverage dropping below 80% | Test debt | Add tests before new features |

### Managing Debt

| Strategy | When | How |
|----------|------|-----|
| **Boy Scout Rule** | Always | Leave code cleaner than you found it (small improvements per PR) |
| **Debt Sprint** | Quarterly | Dedicate one sprint to paying down tracked debt |
| **Tech Debt Budget** | Every sprint | Allocate 15-20% of sprint capacity to debt reduction |
| **Blocking Debt** | Immediately | Architecture test failures, security vulnerabilities block PRs |
| **Tracked Debt** | Backlog | File issues labeled `tech-debt`, prioritize by impact |

### Rules

- Architecture test failures are NEVER allowed to accumulate — fix before merge
- Dependabot PRs are triaged weekly — security updates within 48 hours
- Coverage below 80% blocks the PR (CI enforcement)
- `// TODO` comments without issue links are tech debt — link them or delete
- New features don't create debt by default — follow steering files and patterns

### Tech Debt Register Template

Track in your issue tracker with this structure:

```markdown
**Title:** [Short description]
**Category:** Code | Architecture | Infrastructure | Documentation | Test
**Impact:** High | Medium | Low
**Effort:** Hours | Days | Sprint
**Quality Attributes Affected:** Maintainability, Testability, Security, etc.
**Description:** What's wrong and why it matters
**Proposed Fix:** How to resolve it
**Introduced:** [Date or PR]
**Related Files:** [List]
```

---

## Architecture Evaluation

### Lightweight ATAM (Architecture Trade-off Analysis)

For significant decisions, evaluate trade-offs explicitly:

1. **Identify quality attribute scenarios** (performance, reliability, security, maintainability)
2. **Map decisions to scenarios** — how does each decision affect each quality attribute?
3. **Identify trade-offs** — where does optimizing one quality attribute harm another?
4. **Document in ADR** — record the trade-off and the rationale for the chosen balance

### Trade-offs in This Project

| Decision | Optimizes | Trades Off |
|----------|-----------|-----------|
| Clean Architecture (4 layers) | Maintainability, testability | Indirection overhead for simple CRUD |
| Outbox pattern | Reliability (no event loss) | Eventual consistency (delivery delay) |
| Microservices | Deployability, scalability | Distributed complexity, operational overhead |
| Manual mapping (no AutoMapper) | Debuggability, compile-time safety | More boilerplate for trivial mappings |
| Fixed-window rate limiter | Simplicity | Burst vulnerability at window boundary |
| EF Core (vs. Dapper) | Developer productivity, rich mapping | Abstraction leakage, change tracker overhead |
| MassTransit (vs. raw broker client) | Transport portability, conventions | Abstraction cost, opaque topology |

### Rules

- Every ADR MUST include a "Consequences: Negative" section (no decision is free)
- When two quality attributes conflict, document which one wins and why
- Revisit trade-offs when the context changes (e.g., scaling stage changes)
- Architecture evaluation is not a one-time event — re-evaluate at each scaling stage

---

## Information Hiding (Parnas)

Each module reveals only what consumers need and hides everything else.

### How This Project Applies Information Hiding

| Mechanism | What's Hidden | What's Exposed |
|-----------|---------------|----------------|
| Private domain constructors | Entity creation logic | Static factory methods |
| Private backing fields (`_lines`) | Collection mutability | `IReadOnlyList<T>` |
| Repository interface | EF Core, SQL, connection details | `GetByIdAsync`, `SaveAsync` |
| DTO layer | Domain entity internals | Flat data contract |
| Pipeline behaviours | Logging/validation implementation | `ISender.Send()` |
| Infrastructure layer | Message broker choice, cache provider | `IApplicationEventPublisher` |

### Rules

- Default visibility is `private` or `internal` — only make `public` what's consumed externally
- Domain entities expose behaviour (methods), not state (no public setters)
- Infrastructure implementations are `internal` — only the interface is `public`
- Extension methods in Api/Infrastructure are the only public surface registering services
- Avoid `public` constructors on entities — use factory methods that enforce invariants
