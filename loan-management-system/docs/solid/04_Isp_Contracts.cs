// =============================================================================
// ISP — Interface Segregation Principle (Contracts service)
// "Clients should not depend on interfaces they do not use."
//
// STUDY FILE — self-contained, NOT compiled into the build.
// Contracts is scaffold-only today (just AssemblyMarker), so this is the DESIGNED
// shape for its later deep-dive. The Contracts lifecycle has several clients, each
// needing a DIFFERENT slice of behaviour:
//   - SignContractHandler ....... load one contract, save it       (write side)
//   - ContractExpirySweeper ..... find contracts past the window    (read side)
// Split the fat interface by CLIENT ROLE. One EF class can implement both — ISP is
// about what CLIENTS DEPEND ON, not about one class per interface.
// (Same reason the real code injects ISender in endpoints, not the wider IMediator.)
// =============================================================================

namespace LoanManagement.Study.Solid.Isp;

// -----------------------------------------------------------------------------
// GOOD — focused interfaces, segregated by role.
// -----------------------------------------------------------------------------

/// <summary>Write-side persistence. Everything the command path needs, nothing more.</summary>
public interface IContractRepository
{
    Task<Contract?> GetByIdAsync(ContractId id, CancellationToken ct = default);
    Task AddAsync(Contract contract, CancellationToken ct = default);
    Task SaveChangesAsync(CancellationToken ct = default);
}

/// <summary>Read-only query for expiry sweeping. The sweeper needs THIS and nothing else.</summary>
public interface IExpiringContractQuery
{
    Task<IReadOnlyList<ContractId>> FindPastSigningWindowAsync(DateTime asOfUtc, CancellationToken ct = default);
}

// -----------------------------------------------------------------------------
// One EF class MAY implement several focused interfaces. Segregation constrains
// the consumer's dependency, not the number of implementations.
// -----------------------------------------------------------------------------
public sealed class ContractRepository(IContractStore store)
    : IContractRepository, IExpiringContractQuery
{
    public Task<Contract?> GetByIdAsync(ContractId id, CancellationToken ct = default)
        => store.FindAsync(id, ct);

    public Task AddAsync(Contract contract, CancellationToken ct = default)
        => store.AddAsync(contract, ct);

    public Task SaveChangesAsync(CancellationToken ct = default)
        => store.SaveChangesAsync(ct);

    public Task<IReadOnlyList<ContractId>> FindPastSigningWindowAsync(DateTime asOfUtc, CancellationToken ct = default)
        => store.QueryExpiredIdsAsync(asOfUtc, ct);
}

// -----------------------------------------------------------------------------
// Each client depends ONLY on its slice.
// -----------------------------------------------------------------------------

// Command handler — depends ONLY on the write-side interface. It cannot even see the query.
public sealed class SignContractHandler(IContractRepository repo)
{
    public async Task Handle(SignContractCommand cmd, CancellationToken ct)
    {
        var contract = await repo.GetByIdAsync(new ContractId(cmd.ContractId), ct)
            ?? throw new ContractDomainException("Contract not found.");
        contract.Sign(); // raises ContractSignedEvent
        await repo.SaveChangesAsync(ct);
    }
}

// Background sweeper — depends ONLY on the read query (+ the write repo to mutate).
public sealed class ContractExpirySweeper(IExpiringContractQuery query, IContractRepository repo)
{
    public async Task SweepAsync(CancellationToken ct)
    {
        foreach (var id in await query.FindPastSigningWindowAsync(DateTime.UtcNow, ct))
        {
            var contract = await repo.GetByIdAsync(id, ct);
            contract?.Expire(); // raises ContractExpiredEvent
        }
        await repo.SaveChangesAsync(ct);
    }
}

// DI binds both roles to the one implementation.
public static class ContractsRegistration
{
    public static void AddContracts(ISimpleServiceCollection services)
    {
        services.AddScoped<ContractRepository>();
        services.AddScoped<IContractRepository, ContractRepository>();
        services.AddScoped<IExpiringContractQuery, ContractRepository>();
    }
}

// -----------------------------------------------------------------------------
// ANTI-PATTERN — one FAT interface; every client drags in operations it never calls.
// Test doubles must stub unused methods; some impls throw NotImplementedException
// (the tell-tale sign the interface is too broad); RenderPdf smuggles in a wrong concern.
// -----------------------------------------------------------------------------
public interface IBadContractRepository
{
    Task<Contract?> GetByIdAsync(ContractId id, CancellationToken ct = default);
    Task AddAsync(Contract contract, CancellationToken ct = default);
    Task SaveChangesAsync(CancellationToken ct = default);
    Task<IReadOnlyList<ContractId>> FindPastSigningWindowAsync(DateTime asOf, CancellationToken ct = default);
    Task DeleteAsync(ContractId id, CancellationToken ct = default);                    // no client needs this
    Task BulkImportAsync(IEnumerable<Contract> contracts, CancellationToken ct = default); // definitely not
    Task<byte[]> RenderPdfAsync(ContractId id, CancellationToken ct = default);          // wrong concern entirely
}

// =============================================================================
// Self-contained supporting types (mirror the designed Contract aggregate + conventions).
// =============================================================================
public readonly record struct ContractId(Guid Value);

public enum ContractStatus { Generated, Signed, Expired, Voided }

public sealed class Contract
{
    public ContractId Id { get; private init; }
    public ContractStatus Status { get; private set; }
    private Contract() { }
    public void Sign() => Status = ContractStatus.Signed;
    public void Expire()
    {
        if (Status == ContractStatus.Generated) Status = ContractStatus.Expired;
    }
}

public sealed class ContractDomainException(string message) : Exception(message);

public sealed record SignContractCommand(Guid ContractId);

public interface IContractStore
{
    Task<Contract?> FindAsync(ContractId id, CancellationToken ct = default);
    Task AddAsync(Contract contract, CancellationToken ct = default);
    Task SaveChangesAsync(CancellationToken ct = default);
    Task<IReadOnlyList<ContractId>> QueryExpiredIdsAsync(DateTime asOfUtc, CancellationToken ct = default);
}

public interface ISimpleServiceCollection
{
    void AddScoped<T>();
    void AddScoped<TService, TImpl>() where TImpl : TService;
}
