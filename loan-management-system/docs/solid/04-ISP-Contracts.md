# ISP — Interface Segregation Principle (Contracts service)

> "Clients should not depend on interfaces they do not use."

**Service:** Contracts — loan agreements (owns `Contract`, consumes `CreditDecisionMade`, publishes `ContractGenerated` / `ContractSigned` / `ContractExpired` / `ContractVoided`).
**Status in repo:** Contracts is scaffold-only today (just `AssemblyMarker`), so this is the *designed* shape for its later deep-dive, following the implemented services' conventions (repo interface in Domain, focused method sets, `CancellationToken` on every async method).

---

## Why it matters here

The Contracts lifecycle has several distinct clients, and each needs a *different slice* of behaviour:

| Client | What it actually needs |
|--------|------------------------|
| `SignContractHandler` (command) | load one contract, save it |
| `GetContractStatusQuery` (Portal BFF read) | read a projection — no writes |
| `ContractExpirySweeper` (background job) | find contracts past their signing window |
| `CreditDecisionMadeConsumer` | create a new contract from an approved decision |

If all of that lives on one `IContractRepository`, the command handler is forced to *depend on*
`SearchExpired` and bulk operations it never calls. Depending on methods you don't use couples you
to changes you don't care about: a signature tweak to `SearchExpiredAsync` recompiles and re-tests
the signing handler for no reason. ISP says split the fat interface into role-focused ones — exactly
the steering doc's guidance ("prefer small, focused interfaces… repository interfaces expose only
domain-meaningful operations", "inject `ISender`, not `IMediator`").

## Focused interfaces (segregated by role)

```csharp
namespace LoanManagement.Contracts.Domain;

/// <summary>Write-side persistence for the Contract aggregate. All the command path needs.</summary>
public interface IContractRepository
{
    Task<Contract?> GetByIdAsync(ContractId id, CancellationToken cancellationToken = default);
    Task AddAsync(Contract contract, CancellationToken cancellationToken = default);
    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}

/// <summary>Read-only query for expiry sweeping. The sweeper needs THIS and nothing else.</summary>
public interface IExpiringContractQuery
{
    Task<IReadOnlyList<ContractId>> FindPastSigningWindowAsync(
        DateTime asOfUtc, CancellationToken cancellationToken = default);
}
```

A single EF class can still *implement both* — segregation is about what **clients depend on**, not
about forcing a separate class per interface:

```csharp
namespace LoanManagement.Contracts.Infrastructure.Persistence;

/// <summary>One EF type may implement several focused interfaces; each client depends on only one.</summary>
public sealed class ContractRepository(ContractsDbContext db)
    : IContractRepository, IExpiringContractQuery
{
    public Task<Contract?> GetByIdAsync(ContractId id, CancellationToken ct = default)
        => db.Contracts.FirstOrDefaultAsync(c => c.Id == id, ct);

    public async Task AddAsync(Contract contract, CancellationToken ct = default)
        => await db.Contracts.AddAsync(contract, ct);

    public Task SaveChangesAsync(CancellationToken ct = default)
        => db.SaveChangesAsync(ct);

    public async Task<IReadOnlyList<ContractId>> FindPastSigningWindowAsync(DateTime asOfUtc, CancellationToken ct = default)
        => await db.Contracts
            .Where(c => c.Status == ContractStatus.Generated && c.ExpiresAt < asOfUtc)
            .Select(c => c.Id)
            .ToListAsync(ct);
}
```

Now each client depends only on its slice:

```csharp
// Command handler — depends ONLY on the write-side interface.
public sealed class SignContractHandler(IContractRepository repo)
    : IRequestHandler<SignContractCommand>
{
    public async Task Handle(SignContractCommand cmd, CancellationToken ct)
    {
        var contract = await repo.GetByIdAsync(new ContractId(cmd.ContractId), ct)
            ?? throw new ContractDomainException("Contract not found.");
        contract.Sign();                 // raises ContractSignedEvent
        await repo.SaveChangesAsync(ct);
    }
}

// Background sweeper — depends ONLY on the read query. It cannot even see AddAsync.
public sealed class ContractExpirySweeper(IExpiringContractQuery query, IContractRepository repo)
{
    public async Task SweepAsync(CancellationToken ct)
    {
        foreach (var id in await query.FindPastSigningWindowAsync(DateTime.UtcNow, ct))
        {
            var contract = await repo.GetByIdAsync(id, ct);
            contract?.Expire();          // raises ContractExpiredEvent
        }
        await repo.SaveChangesAsync(ct);
    }
}
```

DI binds both roles to the one implementation:

```csharp
services.AddScoped<ContractRepository>();
services.AddScoped<IContractRepository>(sp => sp.GetRequiredService<ContractRepository>());
services.AddScoped<IExpiringContractQuery>(sp => sp.GetRequiredService<ContractRepository>());
```

---

## Anti-pattern (violates ISP)

```csharp
// BAD — one fat interface; every client drags in operations it never calls.
public interface IContractRepository
{
    Task<Contract?> GetByIdAsync(ContractId id, CancellationToken ct = default);
    Task AddAsync(Contract contract, CancellationToken ct = default);
    Task SaveChangesAsync(CancellationToken ct = default);
    Task<IReadOnlyList<ContractId>> FindPastSigningWindowAsync(DateTime asOf, CancellationToken ct = default);
    Task DeleteAsync(ContractId id, CancellationToken ct = default);          // no client needs this
    Task BulkImportAsync(IEnumerable<Contract> contracts, CancellationToken ct = default); // definitely not
    Task<byte[]> RenderPdfAsync(ContractId id, CancellationToken ct = default); // wrong concern entirely
}
```

Symptoms: test doubles must stub methods the test never exercises; some implementations throw
`NotImplementedException` for `BulkImportAsync` (the steering doc's tell-tale sign the interface is
too broad); and `RenderPdfAsync` smuggles a rendering concern into a persistence contract.

## Interview angle

Say: *"ISP is about keeping clients decoupled from behaviour they don't use. In Contracts, the sign
handler only needs get + save, the expiry sweeper only needs a read query. I split those into
`IContractRepository` and `IExpiringContractQuery` — one EF class can implement both, but each client
depends on just its slice, so a change to the expiry query never recompiles or re-tests the signing
path. It's the same reason the codebase injects `ISender` in endpoints instead of the wider
`IMediator`."*

**Common follow-ups:**
- *Isn't one class implementing two interfaces a code smell?* → No. ISP constrains the *consumer's dependency*, not the number of implementations. Cohesive persistence can live in one class.
- *When would you actually split the class too?* → when the read side scales differently (e.g., a read replica / Dapper query store) — then `IExpiringContractQuery` gets its own implementation, and no client changes because the interface was already segregated.
- *Relation to CQRS?* → ISP naturally pushes you toward separate command/query contracts, which is CQRS at the interface level.

**Pitfalls:**
- Over-segmenting into one-method interfaces everywhere (`IGetContract`, `ISaveContract`, …) creates ceremony without benefit. Split along *client roles*, not per method.
- Putting non-persistence concerns (PDF rendering, notifications) on a repository interface — that's an SRP + ISP violation at once.
