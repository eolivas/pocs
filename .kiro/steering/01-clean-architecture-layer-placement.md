---
inclusion: manual
---

# Clean Architecture Layer Placement

This project follows Clean Architecture with four layers. The dependency rule is strictly enforced via architecture tests (`tests/{SolutionName}.Architecture.Tests/CleanArchitectureTests.cs`).

## Layer Overview

| Layer | Project | Purpose | May Depend On |
|-------|---------|---------|---------------|
| Domain | `{SolutionName}.Domain` | Entities, aggregates, value objects, domain events, repository interfaces | Nothing |
| Application | `{SolutionName}.Application` | Commands, queries, handlers, DTOs, validators, pipeline behaviours, application interfaces | Domain |
| Infrastructure | `{SolutionName}.Infrastructure` | EF Core, MassTransit, HTTP clients, caching, repository implementations | Domain, Application |
| Api | `{SolutionName}.Api` | Minimal API endpoints, middleware, Program.cs host configuration, MCP tools | Domain, Application, Infrastructure |

## What Goes Where

### Domain (`src/{SolutionName}.Domain/`)
- Aggregates, entities, value objects (`Common/`, `ValueObjects/`)
- Strongly-typed IDs (`{Entity}Id.cs`, `CustomerId.cs`, etc.)
- Domain events (`Events/`)
- Domain exceptions (`Exceptions/`)
- Repository interfaces (`I{Entity}Repository.cs`)
- Domain services (`Pricing/`)
- **NEVER** reference Application, Infrastructure, or Api
- **NEVER** use NuGet packages beyond the base SDK (no EF Core, no MediatR, no MassTransit)

### Application (`src/{SolutionName}.Application/`)
- Commands and their handlers (`Commands/`)
- Queries and their handlers (`Queries/`)
- FluentValidation validators (`Commands/` alongside the command)
- DTOs for returning data (`DTOs/`)
- Pipeline behaviours (`Behaviours/`)
- Application-level interfaces for infrastructure concerns (`Interfaces/`)
- **NEVER** reference Infrastructure or Api
- **MAY** reference MediatR, FluentValidation

### Infrastructure (`src/{SolutionName}.Infrastructure/`)
- EF Core DbContext, entity configurations, repository implementations (`Persistence/`)
- MassTransit consumers, event publishers, outbox processor (`Messaging/`)
- HTTP clients and typed clients (`Http/`)
- Caching decorators (`Caching/`)
- Specification pattern implementations (`Specifications/`)
- **NEVER** reference Api

### Api (`src/{SolutionName}.Api/`)
- Minimal API endpoint definitions (`Endpoints/`)
- Middleware (`Middleware/`)
- MCP tool definitions (`Mcp/`)
- `Program.cs` — DI registration, middleware pipeline
- Request/response records (co-located with endpoint classes)

## Decision Checklist

When adding new code, ask:
1. Does it contain business rules or invariants? → **Domain**
2. Does it orchestrate a use case (coordinate domain objects, call repos)? → **Application**
3. Does it talk to an external system (DB, message broker, HTTP API)? → **Infrastructure**
4. Does it handle HTTP requests/responses or host configuration? → **Api**
5. Is it an interface that infrastructure implements? → **Application/Interfaces** (for app-level) or **Domain** (for repo contracts)
