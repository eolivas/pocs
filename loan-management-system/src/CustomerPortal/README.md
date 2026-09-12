# Customer Portal

Bounded context: **Borrower self-service**. Backend for the borrower experience — profile/preferences, saved views, and a secure-message inbox. Builds local read-model projections from loan/contract events for fast portal reads. Owns the `BorrowerProfile` and `MessageThread` aggregates.

A React frontend ([`src/PortalFrontend/frontend`](../PortalFrontend/frontend)) and the BFF ([`src/Bff`](../Bff)) sit in front of this service.

- **Database:** `PortalDb`
- **Publishes:** `BorrowerMessagePosted`, `PreferencesUpdated`
- **Consumes:** `LoanBoarded`, `PaymentApplied`, `ContractSigned` (to keep projections fresh)

## Availability (read-side) — IMPLEMENTED

The Customer Portal is the **read-side** half of the availability goal. It stays available for borrower reads even when Loans/Contracts are down, because it serves from a **local read-model projection** — the read path has zero runtime dependency on other services.

- **`BorrowerLoanView` projection** (`Domain/BorrowerLoanView.cs`) — a local, keyed-by-`LoanId` read model. Not an aggregate; just last-writer state.
- **Idempotent, out-of-order-tolerant consumers** (`Infrastructure/Projections/BorrowerLoanProjection` + `Messaging/{LoanFunded,LoanBoarded}Consumer`) — dedup store + create-or-update upsert. A **monotonic status precedence** (`Boarded > Funded > Unknown`) means a late `LoanFunded` arriving after `LoanBoarded` never regresses the status; a `LoanBoarded` arriving first creates the row and the later `LoanFunded` backfills the amount.
- **Read API** (`GET /api/portal/applications/{applicationId}/loans`) — reads only `PortalDb`. No call to Loans/Contracts at request time = available during their outages.

**Tests** ([`tests/LoanManagement.CustomerPortal.IntegrationTests`](../../tests/LoanManagement.CustomerPortal.IntegrationTests)) prove idempotency (same event twice = one row) and out-of-order tolerance (boarded-before-funded stays `Boarded` and still backfills), on real SQLite.

> Contrast with Origination: the Portal's availability is **read-side** (local projection). Origination's is **write-side** (accept-and-queue). Different problems, different techniques.

See the [root HLD](../../README.md) for the full design. Profile/preferences and secure messaging are a later deep-dive.
