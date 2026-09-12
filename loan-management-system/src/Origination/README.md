# Origination (LOS)

Bounded context: **Loan applications**. Entry point for partner-channel applications; owns the `LoanApplication` aggregate (carries `PartnerId` + `PartnerName`) and kicks off the application-lifecycle saga.

- **Database:** `OriginationDb`
- **Publishes:** `LoanApplicationSubmitted`, `ApplicationRejected`, `ApplicationWithdrawn`
- **Consumes:** `CreditDecisionMade`, `ContractSigned`, `LoanBoarded`
- **Reference service:** this is the pattern the other services follow (4-project Clean Architecture, health endpoints, EF Core + SQL Server, MassTransit + outbox). Its architecture tests live in [`tests/LoanManagement.Origination.Architecture.Tests`](../../tests/LoanManagement.Origination.Architecture.Tests).

## Availability (write-side) — IMPLEMENTED

Origination is the **write-side** half of the availability goal. An applicant's submission must never be lost or blocked by a downstream outage. The technique is **accept-and-queue**, not a read projection:

- **`LoanApplication.Submit()`** validates and records the application locally and raises `LoanApplicationSubmittedEvent`.
- The **outbox** (`Persistence/OriginationDbContext` + `Messaging/OutboxProcessor`) captures that event in the **same transaction** as the write. So `POST /api/applications` succeeds and is durable using only the local DB + outbox — **even if Credit Engine / Contracts are down**. Downstream decisioning happens asynchronously off the event. The endpoint returns `202 Accepted`.
- **Status projection on the aggregate** — idempotent consumers (`Messaging/{CreditDecisionMade,ContractSigned}Consumer`) advance `LoanApplication.Status` (`Decisioned`/`Contracted`) with a **no-regression** rule, so duplicate/out-of-order decision events don't pull the status backwards.
- **Status read** (`GET /api/applications/{id}/status`) reads only `OriginationDb` — available during downstream outages, so the applicant can always see "received / where's my application".

**Tests** ([`tests/LoanManagement.Origination.IntegrationTests`](../../tests/LoanManagement.Origination.IntegrationTests)) prove submission persists the application **and** queues exactly one event with **no downstream running**, and that status transitions are monotonic (no regression).

> Contrast with the Customer Portal: this is **write-side** availability (accept-and-queue). The Portal's is **read-side** (local projection).

See the [root HLD](../../README.md) for the full design, diagrams, and per-service event contracts. Domain logic is a later deep-dive.
