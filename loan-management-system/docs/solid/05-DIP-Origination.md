# DIP — Dependency Inversion Principle (Origination service)

> "High-level modules should not depend on low-level modules. Both depend on abstractions."

**Service:** Origination (LOS) — the reference service and the write-side availability slice.
**Grounded in real code:** `LoanApplication` (`Domain/LoanApplication.cs`), `ILoanApplicationRepository` (`Domain/`), `LoanApplicationRepository` (`Infrastructure/Persistence/`), `SubmitApplicationHandler` (`Application/Commands/`), and the composition root in `Api/Program.cs`.

---

## Why it matters here

DIP is what keeps Clean Architecture's dependency arrows pointing **inward**. In Origination:

- **Domain** (highest-level policy — the `LoanApplication` invariants) defines the abstraction
  `ILoanApplicationRepository`. It has **zero** infrastructure dependencies — it never mentions EF Core.
- **Infrastructure** (lowest-level detail — EF, SQL Server) *implements* that abstraction.
- The **composition root** (`Program.cs`) is the only place that knows both — it wires the concrete
  `LoanApplicationRepository` to the `ILoanApplicationRepository` the handler asks for.

The dependency is *inverted*: instead of the application layer reaching down into EF, EF depends on
an interface owned by the domain. This is precisely what the steering doc mandates ("Domain defines
`I{Entity}Repository` — Infrastructure implements it… never inject concrete classes across layer
boundaries") and what the architecture tests enforce (Domain must not reference Infrastructure).

## The abstraction lives in the inner layer (Domain owns it)

```csharp
// Domain/ILoanApplicationRepository.cs — defined by the HIGH-LEVEL module.
namespace LoanManagement.Origination.Domain;

public interface ILoanApplicationRepository
{
    Task<LoanApplication?> GetByIdAsync(ApplicationId id, CancellationToken cancellationToken = default);
    Task AddAsync(LoanApplication application, CancellationToken cancellationToken = default);
    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}
```

## The handler depends on the abstraction, never on EF

```csharp
// Application/Commands/SubmitApplicationHandler.cs
// Depends on the Domain interface. It has no idea SQL Server or EF Core exist.
public sealed class SubmitApplicationHandler(ILoanApplicationRepository repository)
    : IRequestHandler<SubmitApplicationCommand, Guid>
{
    public async Task<Guid> Handle(SubmitApplicationCommand cmd, CancellationToken ct)
    {
        var application = LoanApplication.Submit(          // domain owns the invariant
            cmd.PartnerId, cmd.PartnerName, cmd.RequestedAmount, cmd.Currency, cmd.TermMonths);

        await repository.AddAsync(application, ct);         // abstraction — impl injected
        await repository.SaveChangesAsync(ct);              // outbox drained in same transaction
        return application.Id.Value;
    }
}
```

## The detail implements the abstraction (Infrastructure depends inward)

```csharp
// Infrastructure/Persistence/LoanApplicationRepository.cs — the LOW-LEVEL detail.
public sealed class LoanApplicationRepository(OriginationDbContext db) : ILoanApplicationRepository
{
    public Task<LoanApplication?> GetByIdAsync(ApplicationId id, CancellationToken ct = default)
        => db.Applications.FirstOrDefaultAsync(a => a.Id == id, ct);

    public async Task AddAsync(LoanApplication application, CancellationToken ct = default)
        => await db.Applications.AddAsync(application, ct);

    public Task SaveChangesAsync(CancellationToken ct = default)
        => db.SaveChangesAsync(ct);
}
```

## The composition root is the ONLY place that knows both

```csharp
// Api/Program.cs (via Add...Infrastructure) — the single spot where abstraction meets concretion.
builder.Services.AddScoped<ILoanApplicationRepository, LoanApplicationRepository>();
builder.Services.AddDbContext<OriginationDbContext>(o => o.UseSqlServer(connectionString));
```

Swapping EF for Dapper, or a real DB for an in-memory test double, is a one-line change *here* — no
domain or application code moves. That's the payoff: the write-side availability tests inject a test
repository and exercise `SubmitApplicationHandler` with no database at all.

---

## Anti-pattern (violates DIP)

```csharp
// BAD — high-level handler reaches DOWN into infrastructure. Arrow points outward.
using LoanManagement.Origination.Infrastructure.Persistence;   // Application now depends on Infra!

public sealed class SubmitApplicationHandler(OriginationDbContext db)   // concrete EF type injected
{
    public async Task<Guid> Handle(SubmitApplicationCommand cmd, CancellationToken ct)
    {
        var application = LoanApplication.Submit(/* ... */);
        await db.Applications.AddAsync(application, ct);           // bound to EF Core forever
        await db.SaveChangesAsync(ct);
        return application.Id.Value;
    }
}
```

Consequences:
- The application layer now **references the Infrastructure project** — the architecture tests
  (NetArchTest) fail the build, because the dependency arrow points the wrong way.
- You cannot unit-test the handler without spinning up a DbContext.
- Replacing the persistence technology means editing business logic.

## Interview angle

Say: *"DIP is the rule that makes Clean Architecture's dependency arrows point inward. In Origination,
the domain owns `ILoanApplicationRepository`; the handler depends on that interface; EF implements it;
and `Program.cs` is the only place that binds the two. So the domain has zero infrastructure
dependencies — I can swap EF for Dapper or an in-memory double by changing one DI line, and I enforce
the direction with architecture tests that fail the build if Domain ever references Infrastructure."*

**Common follow-ups:**
- *Who owns the interface — Application or Domain?* → Here it's Domain (the aggregate's persistence contract). Application-level concerns like an event publisher would be defined in Application. The rule is: the interface belongs to the *consumer's* layer, and the implementer depends on it.
- *Isn't `IRequestHandler` (MediatR) a dependency on a framework?* → Yes, and that's a pragmatic tradeoff — MediatR is a stable abstraction, not a volatile detail. DIP targets volatile low-level details (DB, broker), not every library.
- *How do you actually stop violations?* → build-time architecture tests asserting Domain→(nothing), Application→Domain only.

**Pitfalls:**
- Defining the interface in Infrastructure next to its implementation — that inverts nothing; the abstraction must live in (or above) the layer that consumes it.
- Treating DIP as "always inject an interface." A stable, unlikely-to-change collaborator doesn't always need one — DIP is about decoupling from *volatile* details.
