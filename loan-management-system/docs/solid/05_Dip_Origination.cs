// =============================================================================
// DIP — Dependency Inversion Principle (Origination service)
// "High-level modules should not depend on low-level modules. Both depend on abstractions."
//
// STUDY FILE — self-contained, NOT compiled into the build.
// Grounded in the real Origination code: LoanApplication (Domain), the
// ILoanApplicationRepository abstraction (Domain), LoanApplicationRepository (Infra),
// SubmitApplicationHandler (Application), and the composition root (Api/Program.cs).
//
// DIP keeps Clean Architecture's arrows pointing INWARD:
//   Domain (high-level policy) DEFINES ILoanApplicationRepository — zero infra deps.
//   Infrastructure (EF, SQL) IMPLEMENTS it.
//   Program.cs (composition root) is the ONLY place that knows both concrete + abstract.
// Enforced at build time by NetArchTest (Domain must not reference Infrastructure).
// =============================================================================

namespace LoanManagement.Study.Solid.Dip;

// -----------------------------------------------------------------------------
// The abstraction lives in the INNER layer (Domain owns its persistence contract).
// Real source: src/Origination/LoanManagement.Origination.Domain/ILoanApplicationRepository.cs
// -----------------------------------------------------------------------------
public interface ILoanApplicationRepository
{
    Task<LoanApplication?> GetByIdAsync(ApplicationId id, CancellationToken ct = default);
    Task AddAsync(LoanApplication application, CancellationToken ct = default);
    Task SaveChangesAsync(CancellationToken ct = default);
}

// -----------------------------------------------------------------------------
// GOOD — the high-level handler depends on the ABSTRACTION. It has no idea EF Core
// or SQL Server exist. Real source: Application/Commands/SubmitApplicationHandler.cs
// -----------------------------------------------------------------------------
public sealed class SubmitApplicationHandler(ILoanApplicationRepository repository)
{
    public async Task<Guid> Handle(SubmitApplicationCommand request, CancellationToken ct)
    {
        var application = LoanApplication.Submit(          // domain owns the invariant
            request.PartnerId, request.PartnerName, request.RequestedAmount, request.Currency, request.TermMonths);

        await repository.AddAsync(application, ct);         // abstraction — impl injected via DI
        await repository.SaveChangesAsync(ct);             // outbox drained in the same transaction
        return application.Id.Value;
    }
}

// -----------------------------------------------------------------------------
// The DETAIL implements the abstraction (Infrastructure depends INWARD on Domain).
// Real source: Infrastructure/Persistence/LoanApplicationRepository.cs
// -----------------------------------------------------------------------------
public sealed class LoanApplicationRepository(IOriginationDbContext db) : ILoanApplicationRepository
{
    public Task<LoanApplication?> GetByIdAsync(ApplicationId id, CancellationToken ct = default)
        => db.FindApplicationAsync(id, ct);

    public Task AddAsync(LoanApplication application, CancellationToken ct = default)
        => db.AddApplicationAsync(application, ct);

    public Task SaveChangesAsync(CancellationToken ct = default)
        => db.SaveChangesAsync(ct);
}

// -----------------------------------------------------------------------------
// The composition root is the ONLY place that knows both. Swap EF for Dapper, or a
// real DB for an in-memory test double, by changing THIS line — no domain/app code moves.
// Real wiring: services.AddScoped<ILoanApplicationRepository, LoanApplicationRepository>();
// -----------------------------------------------------------------------------
public static class OriginationRegistration
{
    public static void AddOrigination(ISimpleServiceCollection services)
    {
        services.AddScoped<ILoanApplicationRepository, LoanApplicationRepository>();
        // services.AddDbContext<OriginationDbContext>(o => o.UseSqlServer(connectionString));
    }
}

// -----------------------------------------------------------------------------
// ANTI-PATTERN — the high-level handler reaches DOWN into infrastructure. The
// dependency arrow now points OUTWARD: Application -> Infrastructure. NetArchTest
// fails the build, the handler can't be unit-tested without a DbContext, and
// swapping persistence means editing business logic.
// -----------------------------------------------------------------------------
public sealed class BadSubmitApplicationHandler(OriginationDbContext db) // concrete EF type injected!
{
    public async Task<Guid> Handle(SubmitApplicationCommand request, CancellationToken ct)
    {
        var application = LoanApplication.Submit(
            request.PartnerId, request.PartnerName, request.RequestedAmount, request.Currency, request.TermMonths);

        await db.Applications.AddAsync(application, ct); // bound to EF Core forever
        await db.SaveChangesAsync(ct);
        return application.Id.Value;
    }
}

// =============================================================================
// Self-contained supporting types (mirror real Origination.Domain types).
// =============================================================================
public readonly record struct ApplicationId(Guid Value) { public static ApplicationId New() => new(Guid.NewGuid()); }

public sealed class ApplicationDomainException(string message) : Exception(message);

public enum ApplicationStatus { Submitted, Decisioned, Contracted, Rejected, Withdrawn }

public sealed class LoanApplication
{
    public ApplicationId Id { get; private init; }
    public Guid PartnerId { get; private init; }
    public string PartnerName { get; private init; } = string.Empty;
    public decimal RequestedAmount { get; private init; }
    public string Currency { get; private init; } = string.Empty;
    public int TermMonths { get; private init; }
    public ApplicationStatus Status { get; private set; }

    private LoanApplication() { }

    public static LoanApplication Submit(
        Guid partnerId, string partnerName, decimal requestedAmount, string currency, int termMonths)
    {
        if (partnerId == Guid.Empty)
            throw new ApplicationDomainException("A partner is required to submit an application.");
        if (requestedAmount <= 0)
            throw new ApplicationDomainException("Requested amount must be greater than zero.");
        if (string.IsNullOrWhiteSpace(currency) || currency.Length != 3)
            throw new ApplicationDomainException("Currency must be a 3-letter code.");
        if (termMonths <= 0)
            throw new ApplicationDomainException("Term must be a positive number of months.");

        return new LoanApplication
        {
            Id = ApplicationId.New(),
            PartnerId = partnerId,
            PartnerName = partnerName,
            RequestedAmount = requestedAmount,
            Currency = currency.ToUpperInvariant(),
            TermMonths = termMonths,
            Status = ApplicationStatus.Submitted,
        };
    }
}

public sealed record SubmitApplicationCommand(
    Guid PartnerId, string PartnerName, decimal RequestedAmount, string Currency, int TermMonths);

// The abstraction the real repo depends on (Infra depends inward, not on EF directly here).
public interface IOriginationDbContext
{
    Task<LoanApplication?> FindApplicationAsync(ApplicationId id, CancellationToken ct = default);
    Task AddApplicationAsync(LoanApplication application, CancellationToken ct = default);
    Task SaveChangesAsync(CancellationToken ct = default);
}

// Stand-in for the concrete EF DbContext used ONLY by the anti-pattern above.
public sealed class OriginationDbContext
{
    public FakeSet Applications { get; } = new();
    public Task<int> SaveChangesAsync(CancellationToken ct = default) => Task.FromResult(0);

    public sealed class FakeSet
    {
        public Task AddAsync(LoanApplication application, CancellationToken ct = default) => Task.CompletedTask;
    }
}

public interface ISimpleServiceCollection
{
    void AddScoped<TService, TImpl>() where TImpl : TService;
}
