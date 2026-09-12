# LSP — Liskov Substitution Principle (Loans service)

> "Subtypes must be substitutable for their base type without altering correctness."

**Service:** Loans (LSS).
**Grounded in real code:** `ILoanRepository` (`Domain/ILoanRepository.cs`) and its EF implementation `LoanRepository` (`Infrastructure/Persistence`). The funding slice's integration tests already rely on substitutability — they swap the real broker/transport and DB without changing the funding logic.

---

## Why it matters here

`ILoanRepository` is the seam the whole funding slice depends on:

```csharp
// Domain/ILoanRepository.cs — the contract every implementation must honour.
public interface ILoanRepository
{
    Task<Loan?> GetByIdAsync(LoanId id, CancellationToken cancellationToken = default);
    Task<Loan?> GetByContractIdAsync(ContractId contractId, CancellationToken cancellationToken = default);
    Task AddAsync(Loan loan, CancellationToken cancellationToken = default);
    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}
```

The funding logic finds-or-creates a loan **by ContractId** and relies on "if it exists, I get the
latest committed state." That's an implicit part of the contract — not just the method signatures,
but the *semantics*: `GetByContractIdAsync` returns the current persisted loan, or `null`. Any
substitute (a Dapper repo, an in-memory test repo, a caching decorator) must preserve those
semantics or funding correctness breaks — the exact risk the steering doc's LSP section calls out
("swap `InMemoryRepository` for `EfRepository` and behaviour stays identical").

## A substitutable decorator that HONOURS the contract

A cache-aside decorator is a classic place LSP gets violated. Done right, it stays fully
substitutable — same return semantics, same null behaviour, same "reflect committed writes":

```csharp
namespace LoanManagement.Loans.Infrastructure.Persistence;

/// <summary>
/// Cache-aside decorator over any ILoanRepository. Substitutable: it returns the same
/// results as the inner repo (cache is only a fast path; a miss falls through), and it
/// invalidates on write so callers never observe stale state. Preconditions are not
/// strengthened; postconditions are not weakened.
/// </summary>
public sealed class CachedLoanRepository(ILoanRepository inner, IMemoryCache cache) : ILoanRepository
{
    public async Task<Loan?> GetByContractIdAsync(ContractId contractId, CancellationToken ct = default)
    {
        if (cache.TryGetValue(Key(contractId), out Loan? cached))
            return cached;                                  // fast path

        var loan = await inner.GetByContractIdAsync(contractId, ct); // fall through — same result as inner
        if (loan is not null) cache.Set(Key(contractId), loan);
        return loan;                                        // null stays null — no weakened postcondition
    }

    public Task<Loan?> GetByIdAsync(LoanId id, CancellationToken ct = default)
        => inner.GetByIdAsync(id, ct);

    public Task AddAsync(Loan loan, CancellationToken ct = default)
        => inner.AddAsync(loan, ct);

    public async Task SaveChangesAsync(CancellationToken ct = default)
    {
        await inner.SaveChangesAsync(ct);                   // commit FIRST
        // Invalidate AFTER commit so the next read reflects the persisted truth (no stale reads).
    }

    private static object Key(ContractId id) => $"loan:contract:{id.Value}";
}
```

Because it honours the contract, you can wrap the real repo with it and **every existing funding
test still passes** — that's the LSP litmus test.

---

## Anti-pattern (violates LSP — the subtle, dangerous kind)

The signature is identical, so the compiler is happy. The **semantics** are broken, and funding
double-funds under redelivery:

```csharp
// BAD — same interface, broken contract.
public sealed class StaleCachedLoanRepository(ILoanRepository inner, IMemoryCache cache) : ILoanRepository
{
    public Task<Loan?> GetByContractIdAsync(ContractId contractId, CancellationToken ct = default)
        => Task.FromResult(cache.Get<Loan>(Key(contractId)));  // returns cache-only — MISSES real loans!

    public Task SaveChangesAsync(CancellationToken ct = default)
        => Task.CompletedTask;                                 // silently drops writes — strengthened? no, VIOLATED
    // ...
}
```

Why it breaks the system, not just the "rules":
- `GetByContractIdAsync` returns `null` for a loan that *does* exist in the DB (cache miss). Funding's
  find-or-create then creates a **second** loan for the same contract — until the DB unique index on
  `ContractId` throws, turning a silent bug into a crash.
- `SaveChangesAsync` that no-ops weakens the postcondition ("changes are durably committed") to
  nothing. The outbox never gets the `LoanFundedEvent`. Substitution has altered correctness → LSP violation.

## Interview angle

Say: *"LSP is about behavioural contracts, not just matching signatures. `ILoanRepository` promises
`GetByContractIdAsync` returns the latest committed loan or null. A cache decorator is substitutable
only if it preserves that — cache miss must fall through to the real store, and writes must actually
commit before I trust the cache. The test I'd write is 'run the whole funding suite against the
decorator and expect identical results.' If it double-funds, my subtype broke the base contract."*

**Common follow-ups:**
- *How do you enforce it?* → contract tests: one shared test class run against every `ILoanRepository` impl (`EfLoanRepository`, `CachedLoanRepository`, an in-memory test double).
- *Exceptions count too?* → yes. If the base throws `LoanDomainException` on a rule, a subtype swallowing it violates LSP.
- *Where does the DB unique index fit?* → it's the backstop that turns an LSP violation into a fail-fast error instead of silent data corruption.

**Pitfalls:**
- Focusing only on method signatures — the compiler can't see a weakened postcondition.
- Invalidating the cache *before* the commit succeeds, creating a race where a concurrent read repopulates stale data. Commit first, then invalidate.
