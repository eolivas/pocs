// =============================================================================
// SRP — Single Responsibility Principle (Loans service)
// "A class should have one reason to change."
//
// STUDY FILE — self-contained, NOT compiled into the build. It mirrors the real
// Loans code (Loan.cs, FundingService.cs, OutboxProcessor, LoansDbContext) so the
// principle lands against the actual funding-consistency deep dive (root README §3).
// Everything it needs is defined below; comments point at the real source files.
//
// The funding slice must fund EXACTLY ONCE under at-least-once delivery. That only
// holds because each responsibility lives in exactly one place:
//   - Funding INVARIANTS ............ Loan aggregate        (changes when rules change)
//   - Idempotent ORCHESTRATION ...... FundingService       (changes when delivery strategy changes)
//   - PERSISTENCE ................... LoanConfiguration/Db  (changes when schema changes)
//   - Event DELIVERY ................ OutboxProcessor       (changes when transport/retry changes)
// Four different reasons to change => four types.
// =============================================================================

namespace LoanManagement.Study.Solid.Srp;

// -----------------------------------------------------------------------------
// GOOD #1 — The aggregate owns ONLY the invariant + the fact that funding happened.
// It knows nothing about EF, dedup, or the broker. One reason to change: the rules.
// Real source: src/Loans/LoanManagement.Loans.Domain/Loan.cs
// -----------------------------------------------------------------------------
public sealed class Loan : AggregateRoot<LoanId>
{
    public ContractId ContractId { get; private init; }
    public ApplicationId ApplicationId { get; private init; }
    public Money Principal { get; private init; } = null!;
    public LoanStatus Status { get; private set; }
    public DateTime? FundedAt { get; private set; }

    private Loan() { } // EF

    public static Loan CreatePendingFunding(ContractId contractId, ApplicationId applicationId, Money principal)
    {
        ArgumentNullException.ThrowIfNull(principal);
        if (!principal.IsPositive)
            throw new LoanDomainException("A loan principal must be greater than zero.");

        return new Loan
        {
            Id = LoanId.New(),
            ContractId = contractId,
            ApplicationId = applicationId,
            Principal = principal,
            Status = LoanStatus.PendingFunding,
        };
    }

    public void Fund()
    {
        if (Status is LoanStatus.Funded or LoanStatus.Boarded)
            return; // idempotent no-op — a duplicated command funds once

        if (Status != LoanStatus.PendingFunding)
            throw new LoanDomainException($"Cannot fund a loan in status {Status}.");

        Status = LoanStatus.Funded;
        FundedAt = DateTime.UtcNow;
        RaiseDomainEvent(new LoanFundedEvent(
            Id.Value, ContractId.Value, ApplicationId.Value, Principal.Amount, Principal.Currency));
    }
}

// -----------------------------------------------------------------------------
// GOOD #2 — Orchestration ONLY. Delegates the invariant to the aggregate, dedup to
// the store, persistence + outbox drain to the DbContext. One reason to change: the
// idempotency/consistency strategy.
// Real source: src/Loans/LoanManagement.Loans.Infrastructure/Funding/FundingService.cs
// -----------------------------------------------------------------------------
public sealed class FundingService(
    ILoansDbContext db,
    IDeduplicationStore deduplication) : IFundingService
{
    private const string ConsumerName = "Loans.Funding";

    public async Task FundAsync(ContractSignedIntegrationEvent @event, CancellationToken ct = default)
    {
        if (await deduplication.HasBeenProcessedAsync(@event.EventId, ConsumerName, ct))
            return; // dedup store's responsibility

        var contractId = new ContractId(@event.ContractId);
        var loan = await db.FindLoanByContractAsync(contractId, ct)
                   ?? Loan.CreatePendingFunding(
                        contractId,
                        new ApplicationId(@event.ApplicationId),
                        new Money(@event.Amount, @event.Currency));

        loan.Fund(); // aggregate's responsibility (the invariant)

        await deduplication.MarkProcessedAsync(@event.EventId, ConsumerName, ct);
        await db.SaveChangesAsync(ct); // DbContext's responsibility: persist + drain outbox atomically
    }
}

// -----------------------------------------------------------------------------
// ANTI-PATTERN — one "do everything" handler with FOUR reasons to change.
// It is a dual write (insert + publish with no shared transaction — the exact bug
// the outbox exists to prevent), and its invariant cannot be unit-tested without a DB.
// -----------------------------------------------------------------------------
public sealed class BadFundEverythingHandler(IRawSql sql, IBroker broker)
{
    public async Task Handle(ContractSignedIntegrationEvent e)
    {
        // reason to change #1: business rule leaks out of the aggregate
        if (e.Amount <= 0) throw new InvalidOperationException("bad amount");

        // reason to change #2: raw persistence + dedup inline
        if (await sql.ExistsAsync("SELECT 1 FROM processed_events WHERE ...")) return;
        await sql.ExecuteAsync("INSERT INTO loans ...");

        // reason to change #3: transport coupling with no outbox — if this throws
        // AFTER the insert, state and events drift apart forever.
        await broker.PublishAsync(new LoanFundedEvent(e.LoanId, e.ContractId, e.ApplicationId, e.Amount, e.Currency));
    }
}

// =============================================================================
// Self-contained supporting types (mirror real types in Shared.Kernel + Loans.Domain)
// =============================================================================

// Shared.Kernel — src/Shared/LoanManagement.Shared.Kernel/
public interface IDomainEvent { Guid EventId { get; } DateTime OccurredAt { get; } }

public abstract record DomainEvent : IDomainEvent
{
    public Guid EventId { get; init; } = Guid.NewGuid();
    public DateTime OccurredAt { get; init; } = DateTime.UtcNow;
}

public abstract class Entity<TId> where TId : struct
{
    public TId Id { get; protected init; }
}

public abstract class AggregateRoot<TId> : Entity<TId> where TId : struct
{
    private readonly List<IDomainEvent> _domainEvents = [];
    public IReadOnlyList<IDomainEvent> DomainEvents => _domainEvents.AsReadOnly();
    protected void RaiseDomainEvent(IDomainEvent domainEvent) => _domainEvents.Add(domainEvent);
    public void ClearDomainEvents() => _domainEvents.Clear();
}

public interface IDeduplicationStore
{
    Task<bool> HasBeenProcessedAsync(Guid eventId, string consumer, CancellationToken ct = default);
    Task MarkProcessedAsync(Guid eventId, string consumer, CancellationToken ct = default);
}

// Loans.Domain — strongly-typed IDs, Money, exception, status, events
public readonly record struct LoanId(Guid Value) { public static LoanId New() => new(Guid.NewGuid()); }
public readonly record struct ContractId(Guid Value);
public readonly record struct ApplicationId(Guid Value);

public sealed record Money
{
    public decimal Amount { get; }
    public string Currency { get; }
    public Money(decimal amount, string currency)
    {
        if (amount < 0) throw new ArgumentException("Amount cannot be negative.", nameof(amount));
        if (string.IsNullOrWhiteSpace(currency)) throw new ArgumentException("Currency is required.", nameof(currency));
        Amount = amount;
        Currency = currency.ToUpperInvariant();
    }
    public bool IsPositive => Amount > 0m;
}

public sealed class LoanDomainException(string message) : Exception(message);

public enum LoanStatus { PendingFunding, Funded, Boarded, FundingFailed }

public sealed record LoanFundedEvent(
    Guid LoanId, Guid ContractId, Guid ApplicationId, decimal Amount, string Currency) : DomainEvent;

// Study-only collaborators
public interface ILoansDbContext
{
    Task<Loan?> FindLoanByContractAsync(ContractId contractId, CancellationToken ct = default);
    Task SaveChangesAsync(CancellationToken ct = default);
}

public interface IFundingService
{
    Task FundAsync(ContractSignedIntegrationEvent @event, CancellationToken ct = default);
}

public sealed record ContractSignedIntegrationEvent(
    Guid EventId, Guid ContractId, Guid ApplicationId, Guid LoanId, decimal Amount, string Currency);

public interface IRawSql { Task<bool> ExistsAsync(string sql); Task ExecuteAsync(string sql); }
public interface IBroker { Task PublishAsync(object message); }
