// =============================================================================
// LSP — Liskov Substitution Principle (Loans service)
// "Subtypes must be substitutable for their base type without altering correctness."
//
// STUDY FILE — self-contained, NOT compiled into the build.
// Grounded in the real ILoanRepository (src/Loans/.../ILoanRepository.cs) and its
// EF implementation LoanRepository. The funding slice relies on the CONTRACT of
// GetByContractIdAsync: "return the latest committed loan for this contract, or null."
// A substitute (cache decorator, in-memory double, Dapper repo) is valid ONLY if it
// preserves those SEMANTICS — not just the signatures.
// =============================================================================

namespace LoanManagement.Study.Solid.Lsp;

// -----------------------------------------------------------------------------
// The base contract. Note the SEMANTICS in the doc comments — they are part of
// the contract just as much as the signatures.
// Real source: src/Loans/LoanManagement.Loans.Domain/ILoanRepository.cs
// -----------------------------------------------------------------------------
public interface ILoanRepository
{
    /// <summary>Returns the current committed loan for this id, or null. Never stale.</summary>
    Task<Loan?> GetByIdAsync(LoanId id, CancellationToken ct = default);

    /// <summary>Returns the current committed loan for this contract, or null. Never stale.</summary>
    Task<Loan?> GetByContractIdAsync(ContractId contractId, CancellationToken ct = default);

    Task AddAsync(Loan loan, CancellationToken ct = default);

    /// <summary>Durably commits pending changes. Post-condition: writes are persisted.</summary>
    Task SaveChangesAsync(CancellationToken ct = default);
}

// -----------------------------------------------------------------------------
// GOOD — a cache-aside decorator that HONOURS the contract, so it is fully
// substitutable: cache miss falls through to the inner repo (same result), null
// stays null (no weakened post-condition), writes commit before the cache is trusted.
// Run the whole funding test suite against this and it still passes — the LSP litmus.
// -----------------------------------------------------------------------------
public sealed class CachedLoanRepository(ILoanRepository inner, ICache cache) : ILoanRepository
{
    public async Task<Loan?> GetByContractIdAsync(ContractId contractId, CancellationToken ct = default)
    {
        if (cache.TryGet(Key(contractId), out Loan? cached))
            return cached; // fast path

        var loan = await inner.GetByContractIdAsync(contractId, ct); // fall through — SAME result as inner
        if (loan is not null) cache.Set(Key(contractId), loan);
        return loan; // null stays null — post-condition preserved
    }

    public Task<Loan?> GetByIdAsync(LoanId id, CancellationToken ct = default)
        => inner.GetByIdAsync(id, ct);

    public Task AddAsync(Loan loan, CancellationToken ct = default)
        => inner.AddAsync(loan, ct);

    public async Task SaveChangesAsync(CancellationToken ct = default)
    {
        await inner.SaveChangesAsync(ct);   // commit FIRST
        // invalidate AFTER a successful commit so the next read reflects persisted truth
    }

    private static string Key(ContractId id) => $"loan:contract:{id.Value}";
}

// -----------------------------------------------------------------------------
// ANTI-PATTERN — SAME interface, BROKEN contract. Compiler is happy; the system
// double-funds. This is the subtle, dangerous kind of LSP violation.
// -----------------------------------------------------------------------------
public sealed class StaleCachedLoanRepository(ICache cache) : ILoanRepository
{
    // VIOLATION: returns cache-only. Misses loans that exist in the DB, so funding's
    // find-or-create builds a SECOND loan for the same contract (until the DB unique
    // index on ContractId throws). A weakened post-condition alters correctness.
    public Task<Loan?> GetByContractIdAsync(ContractId contractId, CancellationToken ct = default)
        => Task.FromResult(cache.Get<Loan>($"loan:contract:{contractId.Value}"));

    // VIOLATION: silently drops writes. The LoanFundedEvent never reaches the outbox.
    public Task SaveChangesAsync(CancellationToken ct = default) => Task.CompletedTask;

    public Task<Loan?> GetByIdAsync(LoanId id, CancellationToken ct = default) => Task.FromResult<Loan?>(null);
    public Task AddAsync(Loan loan, CancellationToken ct = default) => Task.CompletedTask;
}

// -----------------------------------------------------------------------------
// ENFORCEMENT — a contract test run against EVERY implementation. If a subtype
// breaks substitutability, the SAME test fails for it. This is how you catch a
// weakened post-condition the compiler cannot see.
// -----------------------------------------------------------------------------
public static class LoanRepositoryContractTests
{
    // Pseudo-test: pass each implementation through the same expectations.
    public static async Task GetByContractId_returns_committed_loan(ILoanRepository repo)
    {
        var contractId = new ContractId(Guid.NewGuid());
        var loan = Loan.CreatePendingFunding(contractId, new ApplicationId(Guid.NewGuid()));
        await repo.AddAsync(loan);
        await repo.SaveChangesAsync();

        var fetched = await repo.GetByContractIdAsync(contractId);
        // EfLoanRepository + CachedLoanRepository PASS; StaleCachedLoanRepository FAILS (returns null).
        Assert(fetched is not null, "must return the committed loan");
    }

    private static void Assert(bool condition, string because)
    {
        if (!condition) throw new InvalidOperationException($"Contract violated: {because}");
    }
}

// =============================================================================
// Self-contained supporting types.
// =============================================================================
public readonly record struct LoanId(Guid Value) { public static LoanId New() => new(Guid.NewGuid()); }
public readonly record struct ContractId(Guid Value);
public readonly record struct ApplicationId(Guid Value);

public sealed class Loan
{
    public LoanId Id { get; private init; }
    public ContractId ContractId { get; private init; }
    public ApplicationId ApplicationId { get; private init; }
    private Loan() { }
    public static Loan CreatePendingFunding(ContractId contractId, ApplicationId applicationId)
        => new() { Id = LoanId.New(), ContractId = contractId, ApplicationId = applicationId };
}

public interface ICache
{
    bool TryGet<T>(string key, out T? value);
    T? Get<T>(string key);
    void Set<T>(string key, T value);
}
