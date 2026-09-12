# Loans (LSS)

Bounded context: **Active loan servicing**. Boards an approved + signed loan, owns the amortization schedule, applies payments, and tracks balance/payoff. The highest-value transactional context. Owns the `Loan` and `RepaymentSchedule` aggregates.

- **Database:** `LoansDb`
- **Publishes:** `LoanFunded`, `LoanBoarded`, `PaymentApplied`, `PaymentMissed`, `LoanPaidOff`
- **Consumes:** `ContractSigned`

## The funding → loan conversion (lifecycle boundary)

This service owns the moment an origination becomes an active loan:

```
ContractSigned  →  LoanFunded  →  LoanBoarded
   (Contracts)      (funds           (loan is active
                     disbursed)        and serviceable)
```

- **`LoanFunded`** — funds have been disbursed for a signed contract. This is the pivot: the customer stops being an *applicant* and becomes a *borrower*.
- **`LoanBoarded`** — the loan is set up for servicing (schedule created, ready for payments).

This conversion is the boundary between the two customer experiences:
- Before it, the customer uses the **Application (Originations) Frontend** via the **Application BFF**.
- After it, the customer uses the **Portal Frontend** via the **Portal BFF**.

**Communications** consumes `LoanFunded` / `LoanBoarded` to notify the borrower that their loan is active and to point them at the portal.

## Funding consistency — IMPLEMENTED

This service is the first with real domain logic: the **funding-consistency vertical slice**. It proves that funding happens **exactly once**, survives duplicate/at-least-once delivery, and compensates on failure — using outbox + saga + idempotency rather than any transport guarantee.

- **`Loan` aggregate** (`Loan.cs`) — funding state machine `PendingFunding → Funded → Boarded` (+ `FundingFailed`). `Fund()` / `Board()` / `FailFunding()` are all idempotent no-ops on repeat, so a duplicated command/event transitions once.
- **Outbox pattern** (`Persistence/LoansDbContext` + `Messaging/OutboxProcessor`) — domain events are written to `outbox_messages` in the **same transaction** as the state change (no dual-write), then published by a background processor with retry → `_error` dead-letter.
- **Idempotent consumption** (`Funding/FundingService` + `Persistence/DeduplicationStore`) — a `processed_events` dedup table plus a **unique index on `ContractId`** guarantees one contract funds one loan, even on redelivery.
- **Funding saga** (`Sagas/FundingStateMachine`) — orchestrates `ContractSigned → [fund] → LoanFunded → [board] → LoanBoarded`, with `LoanFundingFailed → Failed` compensation. It is the single consumer of `ContractSignedIntegrationEvent`.
- **CQRS** — `FundLoanCommand` (idempotent manual trigger) and `GetLoanStatusQuery`.

**Tests** ([`tests/LoanManagement.Loans.Domain.Tests`](../../tests/LoanManagement.Loans.Domain.Tests), [`tests/LoanManagement.Loans.IntegrationTests`](../../tests/LoanManagement.Loans.IntegrationTests)) prove: fund-once emits one event; the **same event twice funds once** (dedup); a **redelivery with a new event id but same contract still funds once**; the saga happy path finalizes; and a funding failure does **not** board the loan. SQLite-backed integration tests exercise the real unique index and transactions.

> The transport is pluggable (see root README §3.2) but funding correctness comes from **outbox + saga + idempotency**, not the broker.

See the [root HLD](../../README.md) for the full design. Amortization and payment logic are a later deep-dive.
