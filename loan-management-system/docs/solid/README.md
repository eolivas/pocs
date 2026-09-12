# SOLID Principles — Architecture Deep Dive

A study companion to the root [HLD](../../README.md) and the [`12-solid-principles.md`](../../../.kiro/steering/12-solid-principles.md) steering doc.

Each principle has **two study artifacts**:

- a **`.cs` file** — self-contained, readable code (good + anti-pattern side by side), the primary
  study material. Not wired into the build; comments point at the real source files.
- a **`.md` file** — the narrative companion: why it matters here, the interview angle, likely
  follow-up questions, and pitfalls.

The examples are grounded in code that actually exists (or, for the scaffold-only services, in the
designed shape their later deep-dive will take), so each principle lands against a concrete domain
rather than a toy `Shape`/`Animal` demo.

This is the **deep-dive layer** the HLD points to: the root README designs *service boundaries and
communication*; these notes go one level down into *how the code inside a service is structured* so it
stays changeable, testable, and correct.

## The five examples

| # | Principle | Service | Code | Notes | The concrete lesson |
|---|-----------|---------|------|-------|---------------------|
| 01 | **S**ingle Responsibility | **Loans** | [`.cs`](01_Srp_Loans.cs) | [`.md`](01-SRP-Loans.md) | Funding correctness holds because invariants (aggregate), idempotent orchestration (`FundingService`), persistence (EF config), and delivery (`OutboxProcessor`) each have exactly one reason to change. |
| 02 | **O**pen/Closed | **Credit Engine** | [`.cs`](02_Ocp_CreditEngine.cs) | [`.md`](02-OCP-CreditEngine.md) | Underwriting rules are `ICreditRule` classes folded by a `CreditPolicy` engine. A new rule is a new class + one DI line — the engine is never edited. |
| 03 | **L**iskov Substitution | **Loans** | [`.cs`](03_Lsp_Loans.cs) | [`.md`](03-LSP-Loans.md) | A `CachedLoanRepository` decorator is substitutable only if it preserves `ILoanRepository` semantics (cache miss falls through, writes commit). Break that and funding double-funds. |
| 04 | **I**nterface Segregation | **Contracts** | [`.cs`](04_Isp_Contracts.cs) | [`.md`](04-ISP-Contracts.md) | The sign handler needs get+save; the expiry sweeper needs a read query. Split into `IContractRepository` + `IExpiringContractQuery` — one class implements both, each client depends on its slice. |
| 05 | **D**ependency Inversion | **Origination** | [`.cs`](05_Dip_Origination.cs) | [`.md`](05-DIP-Origination.md) | Domain owns `ILoanApplicationRepository`; the handler depends on it; EF implements it; `Program.cs` binds them. Arrows point inward, enforced by architecture tests. |

## How the examples map to the services

The four services requested for this session sit on the application lifecycle in this order:

```
Origination  →  Credit Engine  →  Contracts  →  Loans
  (DIP)            (OCP)            (ISP)        (SRP, LSP)
```

- **Origination** and **Loans** are **fully implemented**, so their examples (DIP, SRP, LSP) quote real
  types verbatim: `LoanApplication`, `ILoanApplicationRepository`, `SubmitApplicationHandler`, `Loan`,
  `FundingService`, `ILoanRepository`, `OutboxProcessor`.
- **Credit Engine** and **Contracts** are **scaffold-only** today (each is just an `AssemblyMarker`
  with EF/DI wiring). Their examples (OCP, ISP) show the *designed* shape their later deep-dive should
  take, following the same conventions as the implemented services. They double as a head-start for
  that work.

## Conventions the examples follow

So new code blends into the codebase (confirmed against `Origination` and `Loans`):

- `net9.0`, `Nullable=enable`, `ImplicitUsings=enable`, file-scoped namespaces, `sealed` everywhere.
- Primary constructors for DI (`public sealed class Foo(IBar bar) : IFoo`).
- Aggregates: private EF ctor + `private init`/`private set` + static factory + `RaiseDomainEvent`,
  inheriting `AggregateRoot<TId>` (from `LoanManagement.Shared.Kernel`).
- Strongly-typed IDs: `public readonly record struct XId(Guid Value)`.
- Domain events: `sealed record ... : DomainEvent`, past-tense, carrying IDs + minimal data.
- Repository interface in **Domain**, `sealed` EF implementation in **Infrastructure/Persistence**.
- Every async method takes `CancellationToken cancellationToken = default`.
- **No** `IApplicationEventPublisher` and **no** MediatR pipeline behaviours in real code — events flow
  via the **outbox** drained in `DbContext.SaveChangesAsync`. (The steering doc mentions a publisher
  abstraction; the implemented code uses the outbox instead. The examples follow the real code.)

## The SOLID checklist (from the steering doc)

Before adding code to any of these services, verify:

1. **SRP** — one reason to change per class?
2. **OCP** — extended via new classes, not edits to existing ones?
3. **LSP** — new implementations honour the *full behavioural* contract (not just the signature)?
4. **ISP** — interfaces focused on what each client actually uses?
5. **DIP** — dependencies point inward, toward abstractions the inner layers own?
