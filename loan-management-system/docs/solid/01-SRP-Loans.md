# SRP — Single Responsibility Principle (Loans service)

> "A class should have one reason to change."

**Service:** Loans (LSS) — the funding-consistency deep dive (HLD §3, root README).
**Grounded in real code:** `Loan` aggregate (`Loan.cs`), `FundingService` (`Infrastructure/Funding/FundingService.cs`), `OutboxProcessor`, `LoansDbContext`.

---

## Why it matters here

The funding slice is the most complex flow in the system: it must fund **exactly once** under
at-least-once delivery, survive duplicates, and compensate on failure. That correctness only
holds because each responsibility lives in exactly one place:

| Responsibility | Owner | Reason it would change |
|----------------|-------|------------------------|
| Funding **invariants** (can I fund? board? fail?) | `Loan` aggregate | Business rules change |
| **Idempotent orchestration** (dedup + find-or-create + one transaction) | `FundingService` | Delivery/consistency strategy changes |
| **Persistence** (EF mapping, unique index on ContractId) | `LoanConfiguration` + `LoansDbContext` | Storage/schema changes |
| **Event delivery** (drain outbox → broker, retry, dead-letter) | `OutboxProcessor` | Transport / retry policy changes |

Each of these has a *different reason to change*. The steering doc's SRP table maps exactly onto
this: domain owns invariants, infrastructure owns persistence, one consumer per event.

## The real code already follows SRP

`Loan.Fund()` only knows business rules — nothing about EF, dedup, or the broker:

```csharp
// Loan.cs — the aggregate owns ONLY the invariant + the fact that funding happened.
public void Fund()
{
    if (Status == LoanStatus.Funded || Status == LoanStatus.Boarded)
        return; // idempotent no-op

    if (Status != LoanStatus.PendingFunding)
        throw new LoanDomainException($"Cannot fund a loan in status {Status}.");

    Status = LoanStatus.Funded;
    FundedAt = DateTime.UtcNow;
    RaiseDomainEvent(new LoanFundedEvent(
        Id.Value, ContractId.Value, ApplicationId.Value, Principal.Amount, Principal.Currency));
}
```

`FundingService` only orchestrates — it delegates the invariant to the aggregate, dedup to the
store, and persistence to the DbContext:

```csharp
// FundingService.cs — orchestration only. It does NOT contain business rules or SQL.
public async Task FundAsync(ContractSignedIntegrationEvent @event, CancellationToken ct = default)
{
    if (await deduplication.HasBeenProcessedAsync(@event.EventId, ConsumerName, ct))
        return;                                            // dedup store's job

    var contractId = new ContractId(@event.ContractId);
    var loan = await db.Loans.FirstOrDefaultAsync(l => l.ContractId == contractId, ct)
               ?? Loan.CreatePendingFunding(contractId, new ApplicationId(@event.ApplicationId),
                                            new Money(@event.Amount, @event.Currency));

    loan.Fund();                                           // aggregate's job (the invariant)
    await deduplication.MarkProcessedAsync(@event.EventId, ConsumerName, ct);
    await db.SaveChangesAsync(ct);                         // DbContext's job (persist + drain outbox)
}
```

---

## Anti-pattern (what SRP saves us from)

If we collapsed those responsibilities into one "do everything" handler, it would have **four**
reasons to change and become untestable:

```csharp
// BAD — one class validating, deciding rules, persisting, AND publishing.
public sealed class FundEverythingHandler(IConfiguration config, IBus bus, SqlConnection sql)
{
    public async Task Handle(ContractSignedIntegrationEvent e)
    {
        // reason to change #1: business rule lives here instead of the aggregate
        if (e.Amount <= 0) throw new Exception("bad amount");

        // reason to change #2: raw persistence + dedup inline
        var already = await sql.ExecuteScalarAsync("SELECT 1 FROM processed_events WHERE ...");
        if (already is not null) return;
        await sql.ExecuteAsync("INSERT INTO loans ...");

        // reason to change #3: transport coupling — no outbox, dual-write risk
        await bus.Publish(new LoanFundedEvent(...)); // if this throws AFTER the insert, we drift
    }
}
```

Problems: the DB insert and the publish are a **dual write** (no shared transaction → the outbox
pattern exists precisely to avoid this); the invariant can't be unit-tested without a database;
and a change to the retry policy forces you to touch business logic.

## Interview angle

Say: *"SRP isn't 'one method per class' — it's 'one axis of change per class.' In the funding slice
I separate the invariant (aggregate), the idempotent orchestration (application service), persistence
(EF config), and delivery (outbox processor). The payoff is concrete: because delivery is isolated in
the outbox, I get exactly-once semantics without the aggregate knowing the broker exists."*

**Common follow-ups:**
- *How do you test each responsibility?* → aggregate: pure unit tests (no DB); orchestration: SQLite integration test proving dedup; outbox: a test that a duplicated event drains one message.
- *Isn't `SaveChangesAsync` doing two things (persist + drain outbox)?* → The drain is part of the same atomic commit by design (avoids dual-write); it's one responsibility — "commit state and its events together."

**Pitfalls:**
- Splitting so aggressively that you get anemic classes and orchestration sprawl. SRP balances against cohesion.
- Confusing SRP (reasons to change) with "small files."
