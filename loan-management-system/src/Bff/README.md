# BFFs (Backends for Frontend)

The POC has **one BFF per frontend** — the classic BFF pattern, where each backend is tailored to a specific client. Both are client-aggregation layers: the **only** components permitted to make synchronous cross-service calls. **No domain, no database, no aggregates.**

## Application BFF — `LoanManagement.Bff.Application`

Serves the **pre-funding** [Application (Originations) Frontend](../ApplicationFrontend). Composes the origination journey while a prospective borrower applies, gets a decision, and signs.

- **Composes from:** Origination, Credit Engine, Contracts, Communications
- **Endpoints:** `/health/live`, `/health/ready`, `GET /api/application/status` (placeholder)
- **CORS:** `ApplicationFrontend:Origin` (default `http://localhost:5174`)

## Portal BFF — `LoanManagement.Bff.Portal`

Serves the **post-funding** [Portal Frontend](../PortalFrontend). Composes the borrower self-service experience once the loan is funded and boarded.

- **Composes from:** Loans, Contracts, Customer Portal
- **Endpoints:** `/health/live`, `/health/ready`, `GET /api/portal/dashboard` (placeholder)
- **CORS:** `Portal:Origin` (default `http://localhost:5173`)

## Common

- **Resilience:** every downstream client uses `AddStandardResilienceHandler` (retry with backoff, circuit breaker, request timeout) with a 5s per-client timeout.
- Downstream base addresses are configured under `Services:{Name}`.
- The split follows the customer lifecycle: the **`LoanFunded` → `LoanBoarded`** conversion is the moment a customer graduates from the Application experience to the Portal experience.

## Graceful degradation — IMPLEMENTED

Both BFFs now compose with **graceful degradation**: a *primary* source (the borrower/applicant-facing local read) is treated as always-available, and other services are *optional enrichment*. If an enrichment call fails or its circuit is open, the BFF returns a **partial response with `degraded = true`** rather than a 500 — the customer still sees their data.

- **Portal BFF** — `PortalDashboardComposer` (`GET /api/portal/dashboard/{applicationId}`): primary = Customer Portal projection; optional enrichment = Contracts. Short-lived cache (10s) for complete responses only. Auth and richer aggregation are later deep-dives.
- **Application BFF** — `ApplicationStatusComposer` (`GET /api/application/{applicationId}/status`): primary = Origination local status; optional enrichment = Credit Engine.

**Tests** cover the degradation path: when enrichment throws, the composer returns `Degraded = true` with the primary data intact (see [`tests/LoanManagement.CustomerPortal.IntegrationTests`](../../tests/LoanManagement.CustomerPortal.IntegrationTests) `PortalDashboardComposerTests`).

See the [root HLD](../../README.md) for the full design.
